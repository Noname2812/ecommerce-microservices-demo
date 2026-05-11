using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Kernel.Primitives;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Cache-aside pipeline for queries decorated with <see cref="CacheQueryAttribute"/>.
/// Strategy:
/// 1. Cache hit → return immediately.
/// 2. Cache miss → acquire lock (stampede prevention) → double-check → call handler → store.
/// 3. Negative caching: failure results cached with <see cref="CacheQueryAttribute.NegativeTtlSeconds"/> when enabled.
/// 4. Jitter: expiry is randomised by ±<see cref="CacheQueryAttribute.JitterPercent"/>% to avoid mass expiry.
/// 5. Fail-open: any Redis exception falls back to the handler without surfacing an error.
/// Constrained to <see cref="IQueryBase"/> — commands never instantiate this behavior.
/// </summary>
public sealed class CacheQueryPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase
{
    private static readonly CacheQueryAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<CacheQueryAttribute>();

    private readonly ICacheService _cache;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<CacheQueryPipelineBehavior<TRequest, TResponse>> _logger;

    public CacheQueryPipelineBehavior(
        ICacheService cache,
        IDistributedLockService lockService,
        ILogger<CacheQueryPipelineBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _lockService = lockService;
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
        var expiry = TimeSpan.FromSeconds(_attr.ExpirySeconds);
        var lockExpiry = TimeSpan.FromSeconds(_attr.LockExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(_attr.LockWaitTimeoutSeconds);

        // ── 1. Cache hit ─────────────────────────────────────────────────────
        TResponse? cached;
        try
        {
            cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache read failed for '{Key}' ({Request}). Falling back to handler.",
                cacheKey, typeof(TRequest).Name);
            return await next(ct);
        }

        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for key '{Key}' ({Request})", cacheKey, typeof(TRequest).Name);
            return cached;
        }

        // ── 2. Acquire lock ───────────────────────────────────────────────────
        ILockHandle? lockHandle;
        try
        {
            lockHandle = await _lockService.AcquireAsync(lockKey, lockExpiry, waitTimeout, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Lock acquisition failed for '{Key}' ({Request}). Falling back to handler.",
                cacheKey, typeof(TRequest).Name);
            return await next(ct);
        }

        if (lockHandle is null)
        {
            _logger.LogWarning(
                "Lock wait timeout for cache key '{Key}' after {Timeout}s. Falling back to handler. ({Request})",
                cacheKey, _attr.LockWaitTimeoutSeconds, typeof(TRequest).Name);
            return await next(ct);
        }

        await using (lockHandle)
        {
            // ── 3. Double-check ───────────────────────────────────────────────
            try
            {
                cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cache double-check read failed for '{Key}' ({Request}). Proceeding to handler.",
                    cacheKey, typeof(TRequest).Name);
                cached = default;
            }

            if (cached is not null) return cached;

            // ── 4. Execute handler ────────────────────────────────────────────
            var response = await next(ct);
            var result = response as IResult;

            if (result?.IsSuccess == true)
            {
                // ── 5. Cache success with jitter ──────────────────────────────
                var jitteredExpiry = AddJitter(expiry, _attr.JitterPercent);
                await TrySetCacheAsync(cacheKey, response, jitteredExpiry);
            }
            else if (result?.IsSuccess == false && _attr.NegativeTtlSeconds > 0)
            {
                // ── 6. Negative cache (e.g. not-found) with shorter TTL ───────
                var negativeTtl = TimeSpan.FromSeconds(_attr.NegativeTtlSeconds);
                await TrySetCacheAsync(cacheKey, response, negativeTtl);
            }

            return response;
        }
    }

    private async Task TrySetCacheAsync(string key, TResponse value, TimeSpan ttl)
    {
        try
        {
            // CancellationToken.None: handler already succeeded; client disconnect must not abort the write.
            await _cache.SetAsync(key, value, ttl, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache write failed for '{Key}' ({Request}). Continuing without caching.",
                key, typeof(TRequest).Name);
        }
    }

    private static TimeSpan AddJitter(TimeSpan ttl, int jitterPercent)
    {
        if (jitterPercent <= 0) return ttl;
        // Random.Shared is thread-safe (.NET 6+). Jitter range: [-percent%, +percent%].
        var jitter = ttl.TotalSeconds * jitterPercent / 100.0 * (Random.Shared.NextDouble() * 2 - 1);
        return TimeSpan.FromSeconds(Math.Max(ttl.TotalSeconds + jitter, 1));
    }
}
