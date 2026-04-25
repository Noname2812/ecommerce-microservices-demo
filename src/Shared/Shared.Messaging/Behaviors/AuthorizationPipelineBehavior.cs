using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Reflects [RequirePermission], [RequireRole], [AllowAnonymous] on the request type
/// and validates against the current <see cref="IUserContext"/>.
/// Failures short-circuit the pipeline by wrapping <see cref="AuthorizationErrors"/> into
/// <see cref="Result"/>/<see cref="Result{T}"/>; if TResponse is not a Result type, throws.
/// </summary>
public sealed class AuthorizationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUserContext _user;
    private readonly ILogger<AuthorizationPipelineBehavior<TRequest, TResponse>> _logger;

    public AuthorizationPipelineBehavior(
        IUserContext user,
        ILogger<AuthorizationPipelineBehavior<TRequest, TResponse>> logger)
    {
        _user = user;
        _logger = logger;
    }

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var type = typeof(TRequest);

        if (type.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            return next();

        var requireRoles = type.GetCustomAttributes<RequireRoleAttribute>().ToArray();
        var requirePerms = type.GetCustomAttributes<RequirePermissionAttribute>().ToArray();

        // No attributes → request is anonymous-by-default. Skip checks.
        if (requireRoles.Length == 0 && requirePerms.Length == 0)
            return next();

        if (!_user.IsAuthenticated)
        {
            _logger.LogWarning("Authorization failed for {Request}: unauthenticated", type.Name);
            return Task.FromResult(MakeFailure(AuthorizationErrors.Unauthenticated));
        }

        foreach (var r in requireRoles)
        {
            if (!_user.HasRole(r.Role))
            {
                _logger.LogWarning(
                    "Authorization failed for {Request}: missing role {Role}",
                    type.Name, r.Role);
                return Task.FromResult(MakeFailure(AuthorizationErrors.MissingRole(r.Role)));
            }
        }

        foreach (var p in requirePerms)
        {
            if (_user.Scope < p.MinScope)
            {
                _logger.LogWarning(
                    "Authorization failed for {Request}: scope {ActualScope} < required {RequiredScope} for {Permission}",
                    type.Name, _user.Scope, p.MinScope, p.Permission);
                return Task.FromResult(MakeFailure(AuthorizationErrors.InsufficientScope(p.Permission)));
            }
        }

        return next();
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
            return (TResponse)failureMethod.Invoke(null, new object[] { error })!;
        }

        throw new UnauthorizedAccessException(error.Message);
    }
}
