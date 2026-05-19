using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Cache.Resilience;
using Shared.Kernel.Primitives;
using System.Text.Json;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Pipeline behavior that enforces idempotency for commands implementing <see cref="IIdempotentCommand"/>.
/// Uses a distributed lock to prevent race conditions when concurrent requests share the same key.
/// </summary>
public sealed class IdempotencyPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentCommand
{
    private readonly IDistributedCache _cache;
    private readonly IDistributedLockService _lockService;
    private readonly RedisCircuitBreaker _circuit;
    private readonly ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> _logger;
    private readonly string _cacheKeyPrefix;
    private readonly string _lockKeyPrefix;

    private static readonly string _requestName = typeof(TRequest).Name;

    // Caching is only meaningful when TResponse carries actual data.
    // MediatR's Unit is the equivalent of void — serializing it wastes a Redis round-trip
    // and a lock acquisition for no benefit.
    private static readonly bool ShouldCacheResponse =
        typeof(TResponse) != typeof(Unit);

    // How long to wait for the distributed lock before giving up.
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    // How long the lock is held in Redis even if the process crashes.
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    public IdempotencyPipelineBehavior(
        IDistributedCache cache,
        IDistributedLockService lockService,
        RedisCircuitBreaker circuit,
        IOptions<CacheOptions> cacheOptions,
        ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _lockService = lockService;
        _circuit = circuit;
        _logger = logger;

        var prefix = string.IsNullOrWhiteSpace(cacheOptions.Value.InstanceName)
            ? "urbanx:"
            : $"{cacheOptions.Value.InstanceName}:";
        _cacheKeyPrefix = $"{prefix}idempotency:{_requestName}:";
        _lockKeyPrefix = $"{prefix}idempotency-lock:{_requestName}:";
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!ShouldCacheResponse)
        {
            _logger.LogDebug(
                "Idempotency skipped for {Request} — Unit response carries no data to replay.",
                _requestName);
            return await next(cancellationToken);
        }

        // Circuit open → skip Redis entirely, run handler without idempotency guarantee.
        if (_circuit.ShouldSkipRedis())
        {
            _logger.LogWarning(
                "[Idempotency] Circuit open — skipping idempotency check for {RequestName}. " +
                "Duplicate requests may re-execute.",
                _requestName);
            return await next(cancellationToken);
        }

        var cacheKey = $"{_cacheKeyPrefix}{request.IdempotencyKey}";
        var lockKey = $"{_lockKeyPrefix}{request.IdempotencyKey}";

        // Fast path: check the cache before paying the cost of acquiring a lock.
        // Fail-open: if Redis is unavailable, proceed without idempotency rather than failing the request.
        TResponse? cached;
        try
        {
            cached = await TryGetCachedResponseAsync(cacheKey, cancellationToken);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Idempotency] Cache read failed for {RequestName}. Proceeding without idempotency check.",
                _requestName);
            _circuit.RecordFailure();
            return await next(cancellationToken);
        }

        if (cached is not null)
        {
            return cached;
        }

        // Acquire a distributed lock to serialise concurrent requests that share the same key.
        // Without the lock, two simultaneous requests could both miss the cache and both
        // execute the handler — defeating idempotency.
        ILockHandle? lockHandle;
        try
        {
            lockHandle = await _lockService.AcquireAsync(
                lockKey,
                expiry: LockExpiry,
                waitTimeout: LockWaitTimeout,
                ct: cancellationToken);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Idempotency] Lock acquisition failed for {RequestName}. Proceeding without idempotency guarantee.",
                _requestName);
            _circuit.RecordFailure();
            return await next(cancellationToken);
        }

        if (lockHandle is null)
        {
            _logger.LogWarning(
                "Idempotency lock timed out for {RequestName} key={Key} after {Timeout}. " +
                "Request rejected to avoid duplicate execution.",
                _requestName, lockKey, LockWaitTimeout);

            throw new TimeoutException(
                $"Could not acquire idempotency lock for {_requestName} " +
                $"within {LockWaitTimeout.TotalSeconds}s. " +
                $"A concurrent request with the same IdempotencyKey may still be in progress.");
        }

        await using (lockHandle)
        {
            // Double-check the cache now that we hold the lock.
            // A concurrent request may have already written the result while we were waiting.
            try
            {
                cached = await TryGetCachedResponseAsync(cacheKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Idempotency double-check cache read failed for {RequestName}. Proceeding to handler.",
                    _requestName);
                cached = default;
            }

            if (cached is not null)
            {
                return cached;
            }

            var response = await next(cancellationToken);

            await StoreResponseAsync(cacheKey, response, request);

            return response;
        }
    }

    /// <summary>
    /// Attempts to retrieve and deserialize a previously cached response.
    /// Returns <c>null</c> on cache miss or if the cached entry can no longer be deserialized.
    /// </summary>
    private async Task<TResponse?> TryGetCachedResponseAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cachedJson is null)
            return default;

        if (typeof(TResponse) == typeof(Result<Guid>))
        {
            try
            {
                var trimmed = cachedJson.Trim();
                if (Guid.TryParse(trimmed, out var orderId))
                {
                    _logger.LogInformation(
                        "Idempotency cache hit for {RequestName} key={Key}. Returning cached order id.",
                        _requestName, cacheKey);
                    return (TResponse)(object)Result.Success(orderId);
                }

                var legacy = JsonSerializer.Deserialize<Result<Guid>>(cachedJson);
                if (legacy is { IsSuccess: true })
                {
                    _logger.LogInformation(
                        "Idempotency cache hit (legacy JSON) for {RequestName} key={Key}.",
                        _requestName, cacheKey);
                    return (TResponse)(object)legacy;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Idempotency cache entry for {RequestName} key={Key} could not be read as Result<Guid>. Removing.",
                    _requestName, cacheKey);
            }

            await _cache.RemoveAsync(cacheKey, CancellationToken.None);
            return default;
        }

        try
        {
            var result = JsonSerializer.Deserialize<TResponse>(cachedJson);

            _logger.LogInformation(
                "Idempotency cache hit for {RequestName} key={Key}. Returning cached response.",
                _requestName, cacheKey);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Idempotency cache entry for {RequestName} key={Key} could not be deserialized " +
                "(possible schema change between deployments). " +
                "The stale entry will be removed and the request will be re-executed.",
                _requestName, cacheKey);

            await _cache.RemoveAsync(cacheKey, CancellationToken.None);
            return default;
        }
    }

    /// <summary>
    /// Serializes <paramref name="response"/> and stores it in the distributed cache.
    /// Uses <see cref="CancellationToken.None"/> so a client disconnect does not abort
    /// the write — the handler already succeeded and the idempotency record must be persisted.
    /// </summary>
    private async Task StoreResponseAsync(
        string cacheKey,
        TResponse response,
        TRequest request)
    {
        if (response is IResult { IsFailure: true })
            return;

        // TTL resolution order:
        //   1. Per-command override via IIdempotentCommand.IdempotencyTtl
        //   2. Hard-coded default (24 hours)
        var ttl = request.IdempotencyTtl ?? TimeSpan.FromHours(24);
        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        try
        {
            if (response is Result<Guid> rg)
            {
                await _cache.SetStringAsync(
                    cacheKey,
                    rg.Value.ToString("D"),
                    entryOptions,
                    CancellationToken.None);
            }
            else
            {
                await _cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(response),
                    entryOptions,
                    CancellationToken.None);
            }

            _circuit.RecordSuccess();
            _logger.LogInformation(
                "[Idempotency] Result stored for {RequestName} key={Key} TTL={TTL}.",
                _requestName, cacheKey, ttl);
        }
        catch (Exception ex)
        {
            _circuit.RecordFailure();
            _logger.LogError(ex,
                "[Idempotency] Failed to store result for {RequestName} key={Key}. " +
                "Request succeeded but subsequent duplicate calls will re-execute.",
                _requestName, cacheKey);
        }
    }
}
