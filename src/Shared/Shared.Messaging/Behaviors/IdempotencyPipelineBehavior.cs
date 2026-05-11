using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
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
    private readonly ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> _logger;
    private readonly CacheOptions _cacheOptions;

    // Caching is only meaningful when TResponse carries actual data.
    // MediatR's Unit is the equivalent of void — serializing it wastes a Redis round-trip
    // and a lock acquisition for no benefit.
    private static readonly bool ShouldCacheResponse =
        typeof(TResponse) != typeof(Unit);

    // How long to wait for the distributed lock before giving up.
    // Keeps slow Redis nodes from blocking the request indefinitely.
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    // How long the lock is held in Redis even if the process crashes.
    // Should be comfortably larger than the slowest expected handler execution.
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    public IdempotencyPipelineBehavior(
        IDistributedCache cache,
        IDistributedLockService lockService,
        IOptions<CacheOptions> cacheOptions,
        ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _lockService = lockService;
        _logger = logger;
        _cacheOptions = cacheOptions.Value;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Skip all idempotency logic when the response type carries no data (Unit/void commands).
        if (!ShouldCacheResponse)
        {
            _logger.LogDebug(
                "Idempotency skipped for {Request} — Unit response carries no data to replay.",
                typeof(TRequest).Name);
            return await next();
        }

        // Build cache and lock keys.
        // Prefix is read from CacheOptions.InstanceName so it stays in sync with
        // the Redis instance name configured in AddSharedCache().
        var prefix = string.IsNullOrWhiteSpace(_cacheOptions.InstanceName)
            ? "urbanx:"
            : $"{_cacheOptions.InstanceName}:";

        var cacheKey = $"{prefix}idempotency:{typeof(TRequest).Name}:{request.IdempotencyKey}";
        var lockKey  = $"{prefix}idempotency-lock:{typeof(TRequest).Name}:{request.IdempotencyKey}";

        // Fast path: check the cache before paying the cost of acquiring a lock.
        var cached = await TryGetCachedResponseAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Acquire a distributed lock to serialise concurrent requests that share the same key.
        // Without the lock, two simultaneous requests could both miss the cache and both
        // execute the handler — defeating idempotency.
        var lockHandle = await _lockService.AcquireAsync(
            lockKey,
            expiry: LockExpiry,
            waitTimeout: LockWaitTimeout,
            ct: cancellationToken);

        if (lockHandle is null)
        {
            // Lock acquisition timed out — another request is still holding it.
            // Fail fast rather than executing the handler and producing a duplicate side-effect.
            _logger.LogWarning(
                "Idempotency lock timed out for {RequestName} key={Key} after {Timeout}. " +
                "Request rejected to avoid duplicate execution.",
                typeof(TRequest).Name, lockKey, LockWaitTimeout);

            throw new TimeoutException(
                $"Could not acquire idempotency lock for {typeof(TRequest).Name} " +
                $"within {LockWaitTimeout.TotalSeconds}s. " +
                $"A concurrent request with the same IdempotencyKey may still be in progress.");
        }

        await using (lockHandle)
        {
            // Double-check the cache now that we hold the lock.
            // A concurrent request may have already written the result while we were waiting.
            cached = await TryGetCachedResponseAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            // Execute the actual handler.
            var response = await next();

            // Persist the result so duplicate requests within the TTL window are short-circuited.
            await StoreResponseAsync(cacheKey, response, request, cancellationToken);

            return response;
        }
    }

    /// <summary>
    /// Attempts to retrieve and deserialize a previously cached response.
    /// Returns <c>null</c> on cache miss or if the cached entry can no longer be deserialized
    /// (e.g. after a schema change between deployments).
    /// </summary>
    private async Task<TResponse?> TryGetCachedResponseAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cachedJson is null)
        {
            return default;
        }

        try
        {
            var result = JsonSerializer.Deserialize<TResponse>(cachedJson);

            _logger.LogInformation(
                "Idempotency cache hit for {RequestName} key={Key}. Returning cached response.",
                typeof(TRequest).Name, cacheKey);

            return result;
        }
        catch (JsonException ex)
        {
            // The cached entry cannot be deserialized — most likely a schema change after deployment.
            // Log a warning, remove the stale entry, and fall through to re-execute the handler.
            _logger.LogWarning(ex,
                "Idempotency cache entry for {RequestName} key={Key} could not be deserialized " +
                "(possible schema change between deployments). " +
                "The stale entry will be removed and the request will be re-executed.",
                typeof(TRequest).Name, cacheKey);

            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return default;
        }
    }

    /// <summary>
    /// Serializes <paramref name="response"/> and stores it in the distributed cache.
    /// A failure here is logged but intentionally not re-thrown — the handler has already
    /// succeeded, and dropping the idempotency record is preferable to surfacing an error
    /// to the caller.
    /// </summary>
    private async Task StoreResponseAsync(
        string cacheKey,
        TResponse response,
        TRequest request,
        CancellationToken cancellationToken)
    {
        // TTL resolution order:
        //   1. Per-command override  (IIdempotentCommandWithTtl.IdempotencyTtl)
        //   2. Global setting        (CacheOptions.IdempotencyTtl)
        //   3. Hard-coded default    (24 hours)
        var ttl = request.IdempotencyTtl ?? TimeSpan.FromHours(24);

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);

            _logger.LogInformation(
                "Idempotency result stored for {RequestName} key={Key} TTL={TTL}.",
                typeof(TRequest).Name, cacheKey, ttl);
        }
        catch (Exception ex)
        {
            // Do not let a cache-write failure surface as a handler failure.
            // The request succeeded; subsequent duplicates will simply re-execute
            // until a result is successfully persisted.
            _logger.LogError(ex,
                "Failed to store idempotency result for {RequestName} key={Key}. " +
                "Request succeeded but subsequent duplicate calls will re-execute.",
                typeof(TRequest).Name, cacheKey);
        }
    }
}