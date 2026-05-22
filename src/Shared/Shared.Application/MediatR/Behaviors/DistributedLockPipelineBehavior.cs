using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Cache;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Cache.Resilience;
using Shared.Kernel.Primitives;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Acquires a distributed Redis lock before the handler runs when the request is decorated
/// with <see cref="DistributedLockAttribute"/>.
/// The lock is released automatically after the handler completes (success or failure).
/// When the Redis circuit is open, the lock step is skipped and the handler runs without
/// mutual exclusion (fail-open) to preserve availability.
/// </summary>
public sealed class DistributedLockPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly DistributedLockAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();

    private readonly IDistributedLockService _lockService;
    private readonly RedisCircuitBreaker _circuit;
    private readonly ILogger<DistributedLockPipelineBehavior<TRequest, TResponse>> _logger;

    public DistributedLockPipelineBehavior(
        IDistributedLockService lockService,
        RedisCircuitBreaker circuit,
        ILogger<DistributedLockPipelineBehavior<TRequest, TResponse>> logger)
    {
        _lockService = lockService;
        _circuit = circuit;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_attr is null) return await next(cancellationToken);

        var resource = TemplateResolver.Resolve(_attr.ResourceTemplate, request);
        var expiry = TimeSpan.FromSeconds(_attr.ExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(_attr.WaitTimeoutSeconds);

        // ── Circuit open → fail immediately ─────────────────────────────────
        // Skipping the lock risks concurrent mutation and data corruption.
        // A hard failure is safer than silent loss of mutual exclusion.
        if (_circuit.ShouldSkipRedis())
        {
            _logger.LogError(
                "[DistributedLock] Circuit open — refusing to run '{Request}' without lock on '{Resource}'.",
                typeof(TRequest).Name, resource);
            return ResultHelper.MakeFailure<TResponse>(CacheErrors.LockUnavailable(resource));
        }

        // ── Acquire lock ─────────────────────────────────────────────────────
        ILockHandle? handle;
        try
        {
            handle = await _lockService.AcquireAsync(resource, expiry, waitTimeout, cancellationToken);
            _circuit.RecordSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DistributedLock] Redis lock failed for '{Resource}' ({Request}). " +
                "Refusing to run handler without mutual exclusion.",
                resource, typeof(TRequest).Name);
            _circuit.RecordFailure();
            return ResultHelper.MakeFailure<TResponse>(CacheErrors.LockUnavailable(resource));
        }

        if (handle is null)
        {
            _logger.LogWarning(
                "[DistributedLock] Could not acquire lock for '{Resource}' within {Timeout}s. ({Request})",
                resource, _attr.WaitTimeoutSeconds, typeof(TRequest).Name);
            return ResultHelper.MakeFailure<TResponse>(CacheErrors.LockTimeout(resource));
        }

        await using (handle)
        {
            return await next(cancellationToken);
        }
    }
}
