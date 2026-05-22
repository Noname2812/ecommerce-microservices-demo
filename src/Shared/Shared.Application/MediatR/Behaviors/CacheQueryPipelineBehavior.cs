using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Cache.Resilience;
using Shared.Kernel.Primitives;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

public sealed class CacheQueryPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase
{
    public const string ActivitySourceName = "SharedKernel.Cache";
    public const string MeterName = "SharedKernel.Cache";

    private static readonly ActivitySource _activitySource = new(ActivitySourceName, "1.0.0");
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> _cacheHits =
        _meter.CreateCounter<long>("cache.query.hits", "{requests}", "Cache L2 hits");
    private static readonly Counter<long> _cacheMisses =
        _meter.CreateCounter<long>("cache.query.misses", "{requests}", "Cache L2 misses");
    private static readonly Histogram<double> _l2GetDurationMs =
        _meter.CreateHistogram<double>("cache.query.l2_get_duration_ms", "ms", "Redis GET duration");
    private static readonly Histogram<double> _handlerDurationMs =
        _meter.CreateHistogram<double>("cache.query.handler_duration_ms", "ms", "Handler (DB) duration on cache miss");

    private static readonly CacheQueryAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<CacheQueryAttribute>();

    private static readonly string _requestName = typeof(TRequest).Name;

    // Per TRequest/TResponse pair — each query type has its own flight registry.
    private static readonly ConcurrentDictionary<string, Task<TResponse>> _flights = new();

    private readonly ICacheService _cache;
    private readonly IDistributedLockService _lockService;
    private readonly IMemoryCache _memoryCache;
    private readonly RedisCircuitBreaker _circuit;
    private readonly ILogger<CacheQueryPipelineBehavior<TRequest, TResponse>> _logger;

    public CacheQueryPipelineBehavior(
        ICacheService cache,
        IDistributedLockService lockService,
        IMemoryCache memoryCache,
        RedisCircuitBreaker circuit,
        ILogger<CacheQueryPipelineBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _lockService = lockService;
        _memoryCache = memoryCache;
        _circuit = circuit;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (_attr is null) return await next(ct);

        var cacheKey = TemplateResolver.Resolve(_attr.KeyTemplate, request);

        if (TryGetL1(cacheKey, out var l1)) return l1!;

        // SingleFlight: 300 concurrent misses → 1 leader runs FetchAsync,
        // 299 followers await the same Task. WaitAsync(ct) lets each follower
        // honour its own cancellation without disturbing the leader or other followers.
        return await SingleFlightAsync(cacheKey, () => FetchAsync(cacheKey, next), ct);
    }

    // Leader logic: L1 double-check → Redis (circuit-guarded) → distributed lock → handler.
    private async Task<TResponse> FetchAsync(string cacheKey, RequestHandlerDelegate<TResponse> next)
    {
        if (TryGetL1(cacheKey, out var l1)) return l1!;

        if (_circuit.ShouldSkipRedis())
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("[Cache] Circuit open — skipping Redis for '{Key}' ({Request}).", cacheKey, _requestName);
            return await CallHandlerAsync(cacheKey, next);
        }

        // L2: Redis read — instrumented
        TResponse? cached = default;
        bool redisFailed = false;
        var l2GetSw = Stopwatch.StartNew();
        using (var l2Span = _activitySource.StartActivity("cache.l2.get", ActivityKind.Client))
        {
            l2Span?.SetTag("cache.key", cacheKey);
            l2Span?.SetTag("cache.request", _requestName);
            try
            {
                cached = await _cache.GetAsync<TResponse>(cacheKey, CancellationToken.None);
                _circuit.RecordSuccess();
                l2Span?.SetTag("cache.hit", cached is not null);
            }
            catch (Exception ex)
            {
                l2Span?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex, "[Cache] Redis read failed for '{Key}' ({Request}).", cacheKey, _requestName);
                _circuit.RecordFailure();
                redisFailed = true;
            }
        }
        l2GetSw.Stop();
        _l2GetDurationMs.Record(l2GetSw.Elapsed.TotalMilliseconds,
            new TagList { { "request", _requestName }, { "hit", (!redisFailed && cached is not null).ToString() } });

        if (redisFailed)
            return await CallHandlerAsync(cacheKey, next);

        if (cached is not null)
        {
            _cacheHits.Add(1, new TagList { { "request", _requestName } });
            SetL1(cacheKey, cached);
            return cached;
        }

        _cacheMisses.Add(1, new TagList { { "request", _requestName } });

        // Distributed lock: guards cross-process stampede.
        // SingleFlight already ensures only one in-process goroutine reaches here.
        var lockKey = $"lock:{cacheKey}";
        var lockExpiry = TimeSpan.FromSeconds(_attr!.LockExpirySeconds);
        var lockWait = TimeSpan.FromSeconds(_attr.LockWaitTimeoutSeconds);

        ILockHandle? lockHandle;
        using (var lockSpan = _activitySource.StartActivity("cache.lock.acquire", ActivityKind.Internal))
        {
            lockSpan?.SetTag("cache.lock.key", lockKey);
            lockSpan?.SetTag("cache.lock.wait_timeout_s", _attr.LockWaitTimeoutSeconds);
            try
            {
                lockHandle = await _lockService.AcquireAsync(lockKey, lockExpiry, lockWait, CancellationToken.None);
                _circuit.RecordSuccess();
                lockSpan?.SetTag("cache.lock.acquired", lockHandle is not null);
            }
            catch (Exception ex)
            {
                lockSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex, "[Cache] Redis lock failed for '{Key}' ({Request}).", cacheKey, _requestName);
                _circuit.RecordFailure();
                return await CallHandlerAsync(cacheKey, next);
            }
        }

        if (lockHandle is null)
        {
            // LockWaitTimeoutSeconds=0 → always null; or another process holds the lock.
            _logger.LogWarning("[Cache] Lock timeout for '{Key}' ({Request}) — re-checking L2.", cacheKey, _requestName);
            try
            {
                var l2 = await _cache.GetAsync<TResponse>(cacheKey, CancellationToken.None);
                if (l2 is not null) { SetL1(cacheKey, l2); return l2; }
            }
            catch { /* Redis error: fall through to handler */ }

            return await CallHandlerAsync(cacheKey, next);
        }

        await using (lockHandle)
        {
            // Double-check L2 under lock (another process may have populated it).
            try
            {
                var l2 = await _cache.GetAsync<TResponse>(cacheKey, CancellationToken.None);
                _circuit.RecordSuccess();
                if (l2 is not null) { SetL1(cacheKey, l2); return l2; }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Cache] Redis double-check failed for '{Key}' ({Request}).", cacheKey, _requestName);
                _circuit.RecordFailure();
            }

            return await CallHandlerAsync(cacheKey, next);
        }
    }

    // Calls the handler (DB). On success, populates L2 + L1.
    // On DB failure, logs the error and re-throws — SingleFlight propagates to all followers.
    private async Task<TResponse> CallHandlerAsync(string cacheKey, RequestHandlerDelegate<TResponse> next)
    {
        TResponse response;
        var handlerSw = Stopwatch.StartNew();
        using (var handlerSpan = _activitySource.StartActivity("cache.handler", ActivityKind.Internal))
        {
            handlerSpan?.SetTag("cache.key", cacheKey);
            handlerSpan?.SetTag("cache.request", _requestName);
            try
            {
                response = await next(CancellationToken.None);
                handlerSpan?.SetTag("handler.success", true);
            }
            catch (Exception ex)
            {
                handlerSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "[Cache] Handler failed (DB down?) for '{Key}' ({Request}).", cacheKey, _requestName);
                throw;
            }
        }
        handlerSw.Stop();
        _handlerDurationMs.Record(handlerSw.Elapsed.TotalMilliseconds,
            new TagList { { "request", _requestName } });

        if (response is not IResult result || result.IsSuccess)
        {
            var ttl = AddJitter(TimeSpan.FromSeconds(_attr!.ExpirySeconds), _attr.JitterPercent);
            await TrySetL2Async(cacheKey, response, ttl);
            SetL1(cacheKey, response);
        }
        else if (_attr!.NegativeTtlSeconds > 0)
        {
            var ttl = TimeSpan.FromSeconds(_attr.NegativeTtlSeconds);
            await TrySetL2Async(cacheKey, response, ttl);
            SetL1(cacheKey, response);
        }

        return response;
    }

    // ── SingleFlight ─────────────────────────────────────────────────────────
    private static async Task<TResponse> SingleFlightAsync(
        string key, Func<Task<TResponse>> factory, CancellationToken requestCt)
    {
        var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = _flights.GetOrAdd(key, tcs.Task);

        if (!ReferenceEquals(task, tcs.Task))
            return await task.WaitAsync(requestCt);

        try
        {
            var result = await factory();
            tcs.SetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _flights.TryRemove(new KeyValuePair<string, Task<TResponse>>(key, tcs.Task));
        }
    }

    // ── L1 helpers ───────────────────────────────────────────────────────────
    private bool TryGetL1(string key, out TResponse? value)
    {
        if (_attr!.MemoryTtlSeconds <= 0) { value = default; return false; }
        return _memoryCache.TryGetValue(key, out value);
    }

    private void SetL1(string key, TResponse value)
    {
        if (_attr!.MemoryTtlSeconds <= 0) return;
        _memoryCache.Set(key, value, TimeSpan.FromSeconds(_attr.MemoryTtlSeconds));
    }

    // ── L2 helpers ───────────────────────────────────────────────────────────
    private async Task TrySetL2Async(string key, TResponse value, TimeSpan ttl)
    {
        try
        {
            await _cache.SetAsync(key, value, ttl, CancellationToken.None);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Redis write failed for '{Key}' ({Request}).", key, _requestName);
            _circuit.RecordFailure();
        }
    }

    private static TimeSpan AddJitter(TimeSpan ttl, int jitterPercent)
    {
        if (jitterPercent <= 0) return ttl;
        var jitter = ttl.TotalSeconds * jitterPercent / 100.0 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromSeconds(Math.Max(ttl.TotalSeconds + jitter, 1));
    }
}
