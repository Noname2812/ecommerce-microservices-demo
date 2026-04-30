using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Cache;
using Shared.Cache.Abstractions;
using Shared.Cache.Attributes;
using Shared.Kernel.Primitives;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Acquires a distributed Redis lock before the handler runs when the request is decorated
/// with <see cref="DistributedLockAttribute"/>.
/// The lock is released automatically after the handler completes (success or failure).
/// </summary>
public sealed class DistributedLockPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
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
        var attr = typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();
        if (attr is null) return await next();

        var resource = ResolveResource(attr.ResourceTemplate, request);
        var expiry = TimeSpan.FromSeconds(attr.ExpirySeconds);
        var waitTimeout = TimeSpan.FromSeconds(attr.WaitTimeoutSeconds);

        var handle = await _lockService.AcquireAsync(resource, expiry, waitTimeout, cancellationToken);

        if (handle is null)
        {
            _logger.LogWarning(
                "Could not acquire distributed lock for resource '{Resource}' within {Timeout}s. Request: {Request}",
                resource, attr.WaitTimeoutSeconds, typeof(TRequest).Name);
            return MakeFailure(CacheErrors.LockTimeout(resource));
        }

        await using (handle)
        {
            return await next();
        }
    }

    private static string ResolveResource(string template, TRequest request)
    {
        return Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            var propName = m.Groups[1].Value;
            var prop = typeof(TRequest).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(request)?.ToString() ?? m.Value;
        });
    }

    private static TResponse MakeFailure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        if (typeof(TResponse).IsGenericType
            && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = typeof(TResponse).GetGenericArguments()[0];
            var failureMethod = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
                .MakeGenericMethod(innerType);
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"Distributed lock timed out for '{error.Message}'. " +
            "TResponse is not a Result type — cannot return a failure value.");
    }
}
