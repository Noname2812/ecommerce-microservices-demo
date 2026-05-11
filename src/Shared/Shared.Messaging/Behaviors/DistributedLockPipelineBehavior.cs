using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Cache;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Kernel.Primitives;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Acquires a distributed Redis lock before the handler runs when the request is decorated
/// with <see cref="DistributedLockAttribute"/>.
/// The lock is released automatically after the handler completes (success or failure).
/// </summary>
public sealed class DistributedLockPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly DistributedLockAttribute? _attr =
        typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();

    private readonly IDistributedLockService _lockService;
    private readonly ILogger<DistributedLockPipelineBehavior<TRequest, TResponse>> _logger;

    public DistributedLockPipelineBehavior(
        IDistributedLockService lockService,
        ILogger<DistributedLockPipelineBehavior<TRequest, TResponse>> logger)
    {
        _lockService = lockService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_attr is null) return await next();

        var resource = TemplateResolver.Resolve(_attr.ResourceTemplate, request);
        var expiry = TimeSpan.FromSeconds(_attr.ExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(_attr.WaitTimeoutSeconds);

        var handle = await _lockService.AcquireAsync(resource, expiry, waitTimeout, cancellationToken);

        if (handle is null)
        {
            _logger.LogWarning(
                "Could not acquire distributed lock for resource '{Resource}' within {Timeout}s. Request: {Request}",
                resource, _attr.WaitTimeoutSeconds, typeof(TRequest).Name);
            return ResultHelper.MakeFailure<TResponse>(CacheErrors.LockTimeout(resource));
        }

        await using (handle)
        {
            return await next();
        }
    }
}
