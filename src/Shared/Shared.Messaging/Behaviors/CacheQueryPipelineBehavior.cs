using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Cache-aside pipeline for queries decorated with <see cref="CacheQueryAttribute"/>.
/// Strategy:
/// 1. Check Redis — return immediately on hit.
/// 2. Cache miss: attempt a non-blocking lock (<see cref="IDistributedLockService.TryAcquireAsync"/>).
///    - Lock acquired  → double-check cache → call handler → store result → release.
///    - Lock contested → wait for lock (<see cref="IDistributedLockService.AcquireAsync"/>),
///      then re-read cache (populated by the winner) → return cached value.
/// 3. Wait timeout    → fall back to handler directly (fail-open, logs a warning).
/// Only successful <c>Result&lt;T&gt;</c> responses are cached.
/// </summary>
public sealed class CacheQueryPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
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
        var attr = typeof(TRequest).GetCustomAttribute<CacheQueryAttribute>();
        if (attr is null) return await next(ct);

        if (!IsResultType(typeof(TResponse))) return await next(ct);

        var cacheKey = ResolveKey(attr.KeyTemplate, request);
        var lockKey = $"lock:{cacheKey}";
        var expiry = TimeSpan.FromSeconds(attr.ExpirySeconds);
        var lockExpiry = TimeSpan.FromSeconds(attr.LockExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(attr.LockWaitTimeoutSeconds);

        // ── 1. Cache hit ─────────────────────────────────────────────────────
        var cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for key '{Key}' ({Request})", cacheKey, typeof(TRequest).Name);
            return cached;
        }

        // ── 2. Acquire lock — first request gets it immediately (AcquireAsync
        //       calls TryAcquireAsync on the first iteration), concurrent requests
        //       wait; all go through the same double-check after acquiring.
        var lockHandle = await _lockService.AcquireAsync(lockKey, lockExpiry, waitTimeout, ct);

        if (lockHandle is null)
        {
            _logger.LogWarning(
                "Lock wait timeout for cache key '{Key}' after {Timeout}s. Falling back to handler. ({Request})",
                cacheKey, attr.LockWaitTimeoutSeconds, typeof(TRequest).Name);
            return await next(ct);
        }

        await using (lockHandle)
        {
            // Double-check: a waiting request will find the cache already populated.
            cached = await _cache.GetAsync<TResponse>(cacheKey, ct);
            if (cached is not null) return cached;

            var response = await next(ct);
            if (IsSuccess(response))
                await _cache.SetAsync(cacheKey, response, expiry, ct);
            return response;
        }
    }

    private static string ResolveKey(string template, TRequest request)
    {
        return Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            var propName = m.Groups[1].Value;
            var prop = typeof(TRequest).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(request)?.ToString() ?? m.Value;
        });
    }

    private static bool IsResultType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Shared.Kernel.Primitives.Result<>);

    private static bool IsSuccess(TResponse response)
    {
        if (response is null) return false;
        var prop = typeof(TResponse).GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(response) is true;
    }
}
