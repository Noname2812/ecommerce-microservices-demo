using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Kernel.Primitives;
using System.Collections.Concurrent;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Cache-aside pipeline for queries decorated with <see cref="CacheQueryAttribute"/>.
///
/// Read path (per request):
///   L1 (MemoryCache, in-process) → L2 (Redis, circuit-guarded) → DB handler
///
/// Stampede prevention:
///   L2 up  : Redis distributed lock (cross-process, double-checked).
///   L2 down: In-process SingleFlight — coalesces concurrent misses to one handler call.
///
/// Resilience:
///   Circuit Breaker (<see cref="RedisCircuitBreaker"/>): tracks consecutive Redis failures;
///   opens after threshold; allows probe requests after cooldown.
///   When open → skip Redis, fall through to SingleFlight + L1 only.
///
/// Other behaviours preserved:
///   Negative caching, jitter, fail-open on any Redis error.
/// </summary>
public sealed class CacheQueryPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase
{
    private static readonly CacheQueryAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<CacheQueryAttribute>();

    // Per generic-type instantiation: each TRequest/TResponse pair has its own flight registry.
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
        var lockKey = $"lock:{cacheKey}";
        var l2Expiry = TimeSpan.FromSeconds(_attr.ExpirySeconds);
        var lockExpiry = TimeSpan.FromSeconds(_attr.LockExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(_attr.LockWaitTimeoutSeconds);

        // ── 1. L1: in-process memory cache ──────────────────────────────────
        if (TryGetL1(cacheKey, out var l1)) return l1!;

        // ── 2. Circuit open → skip Redis, SingleFlight to handler ────────────
        if (_circuit.ShouldSkipRedis())
        {
            _logger.LogDebug(
                "[Cache] Circuit open — SingleFlight fallback for '{Key}' ({Request}).",
                cacheKey, typeof(TRequest).Name);
            return await SingleFlightAsync(cacheKey, async () =>
            {
                if (TryGetL1(cacheKey, out var l1b)) return l1b!;
                var r = await next(ct);
                SetL1IfCacheable(cacheKey, r);
                return r;
            });
        }

        // ── 3. L2: Redis read ────────────────────────────────────────────────
        TResponse? cached;
        try
        {
            cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Cache] Redis read failed for '{Key}' ({Request}). Circuit fallback.",
                cacheKey, typeof(TRequest).Name);
            _circuit.RecordFailure();
            return await SingleFlightAsync(cacheKey, async () =>
            {
                if (TryGetL1(cacheKey, out var l1c)) return l1c!;
                var r = await next(ct);
                SetL1IfCacheable(cacheKey, r);
                return r;
            });
        }

        if (cached is not null)
        {
            _logger.LogDebug("[Cache] L2 hit for '{Key}' ({Request})", cacheKey, typeof(TRequest).Name);
            SetL1(cacheKey, cached);
            return cached;
        }

        // ── 4. Acquire Redis lock (stampede prevention) ──────────────────────
        ILockHandle? lockHandle;
        try
        {
            lockHandle = await _lockService.AcquireAsync(lockKey, lockExpiry, waitTimeout, ct);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Cache] Redis lock failed for '{Key}' ({Request}). Circuit fallback.",
                cacheKey, typeof(TRequest).Name);
            _circuit.RecordFailure();
            return await SingleFlightAsync(cacheKey, async () =>
            {
                if (TryGetL1(cacheKey, out var l1d)) return l1d!;
                var r = await next(ct);
                SetL1IfCacheable(cacheKey, r);
                return r;
            });
        }

        if (lockHandle is null)
        {
            // Redis is up but the lock is held by another process — direct fallback.
            _logger.LogWarning(
                "[Cache] Lock timeout for '{Key}' after {Timeout}s — direct handler fallback. ({Request})",
                cacheKey, _attr.LockWaitTimeoutSeconds, typeof(TRequest).Name);
            return await next(ct);
        }

        await using (lockHandle)
        {
            // ── 5. Double-check L2 ─────────────────────────────────────────
            try
            {
                cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
                _circuit.RecordSuccess();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Cache] Redis double-check failed for '{Key}' ({Request}). Proceeding to handler.",
                    cacheKey, typeof(TRequest).Name);
                _circuit.RecordFailure();
                cached = default;
            }

            if (cached is not null)
            {
                SetL1(cacheKey, cached);
                return cached;
            }

            // ── 6. Call handler, populate both caches ─────────────────────
            var response = await next(ct);
            var result = response as IResult;

            if (result?.IsSuccess == true)
            {
                var jitteredExpiry = AddJitter(l2Expiry, _attr.JitterPercent);
                await TrySetL2Async(cacheKey, response, jitteredExpiry);
                SetL1(cacheKey, response);
            }
            else if (result?.IsSuccess == false && _attr.NegativeTtlSeconds > 0)
            {
                var negativeTtl = TimeSpan.FromSeconds(_attr.NegativeTtlSeconds);
                await TrySetL2Async(cacheKey, response, negativeTtl);
                SetL1(cacheKey, response);
            }

            return response;
        }
    }

    // ── SingleFlight ────────────────────────────────────────────────────────
    // When Redis is unavailable, coalesces concurrent requests for the same key
    // into a single handler call. Leader executes; followers await the same Task.
    // If the leader fails, followers surface the same exception (fail-fast).
    private async Task<TResponse> SingleFlightAsync(string key, Func<Task<TResponse>> factory)
    {
        var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = _flights.GetOrAdd(key, tcs.Task);

        if (!ReferenceEquals(task, tcs.Task))
        {
            // Follower: wait for the leader's result.
            _logger.LogDebug("[Cache] SingleFlight follower waiting on '{Key}' ({Request}).",
                key, typeof(TRequest).Name);
            return await task;
        }

        // Leader: execute factory and broadcast result/exception to all followers.
        _logger.LogDebug("[Cache] SingleFlight leader for '{Key}' ({Request}).",
            key, typeof(TRequest).Name);
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

    // ── L1 helpers ──────────────────────────────────────────────────────────
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

    // Sets L1 only when the response would also be stored in L2 (mirrors L2 cacheability rules).
    private void SetL1IfCacheable(string key, TResponse response)
    {
        if (_attr!.MemoryTtlSeconds <= 0) return;
        var result = response as IResult;
        if (result?.IsSuccess == true ||
            (result?.IsSuccess == false && _attr.NegativeTtlSeconds > 0))
        {
            SetL1(key, response);
        }
    }

    // ── L2 helpers ──────────────────────────────────────────────────────────
    private async Task TrySetL2Async(string key, TResponse value, TimeSpan ttl)
    {
        try
        {
            await _cache.SetAsync(key, value, ttl, CancellationToken.None);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Cache] Redis write failed for '{Key}' ({Request}).",
                key, typeof(TRequest).Name);
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
