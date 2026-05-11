using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using System.Linq.Expressions;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Cache-aside pipeline for queries decorated with <see cref="CacheQueryAttribute"/>.
/// Strategy:
/// 1. Check Redis — return immediately on hit.
/// 2. Cache miss: acquire lock → double-check cache → call handler → store result → release.
///    Concurrent requests wait for the lock and read from cache (stampede prevention).
/// 3. Wait timeout → fall back to handler directly (fail-open, logs a warning).
/// Only successful <c>Result&lt;T&gt;</c> responses are cached.
/// Constrained to <see cref="IQueryBase"/> — commands never instantiate this behavior.
/// </summary>
public sealed class CacheQueryPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase
{
    // Static fields are per (TRequest, TResponse) generic instantiation — safe to cache.
    private static readonly CacheQueryAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<CacheQueryAttribute>();

    // Compile once: (TResponse r) => r.IsSuccess
    // IQuery<T> always returns Result<T>, so IsSuccess always exists.
    private static readonly Func<TResponse, bool> _isSuccess = BuildIsSuccess();

    private static Func<TResponse, bool> BuildIsSuccess()
    {
        var prop = typeof(TResponse).GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
        if (prop?.PropertyType != typeof(bool)) return _ => false;
        var param = Expression.Parameter(typeof(TResponse));
        var body = Expression.Property(param, prop);
        return Expression.Lambda<Func<TResponse, bool>>(body, param).Compile();
    }

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
        var cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for key '{Key}' ({Request})", cacheKey, typeof(TRequest).Name);
            return cached;
        }

        // ── 2. Acquire lock — first request wins; concurrent requests wait,
        //       then re-read cache (stampede prevention / double-check locking).
        var lockHandle = await _lockService.AcquireAsync(lockKey, lockExpiry, waitTimeout, ct);

        if (lockHandle is null)
        {
            _logger.LogWarning(
                "Lock wait timeout for cache key '{Key}' after {Timeout}s. Falling back to handler. ({Request})",
                cacheKey, _attr.LockWaitTimeoutSeconds, typeof(TRequest).Name);
            return await next(ct);
        }

        await using (lockHandle)
        {
            cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
            if (cached is not null) return cached;

            var response = await next(ct);
            if (_isSuccess(response))
                await _cache.SetAsync(cacheKey, response, expiry, ct);
            return response;
        }
    }
}
