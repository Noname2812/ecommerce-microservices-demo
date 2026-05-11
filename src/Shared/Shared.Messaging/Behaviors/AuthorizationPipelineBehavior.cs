using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

public sealed class AuthorizationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Cached per TRequest type instantiation — safe because attribute sets are immutable.
    private static readonly bool _isAnonymous =
        typeof(TRequest).GetCustomAttribute<AllowAnonymousAttribute>() is not null;
    private static readonly RequireRoleAttribute[] _roles =
        typeof(TRequest).GetCustomAttributes<RequireRoleAttribute>().ToArray();
    private static readonly RequirePermissionAttribute[] _perms =
        typeof(TRequest).GetCustomAttributes<RequirePermissionAttribute>().ToArray();
    private static readonly bool _hasNoAuthRequirements = _roles.Length == 0 && _perms.Length == 0;

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
        if (_isAnonymous || _hasNoAuthRequirements)
            return next();

        if (!_user.IsAuthenticated)
        {
            _logger.LogWarning("Authorization failed for {Request}: unauthenticated", typeof(TRequest).Name);
            return Task.FromResult(ResultHelper.MakeFailure<TResponse>(AuthorizationErrors.Unauthenticated));
        }

        foreach (var r in _roles)
        {
            if (!_user.HasRole(r.Role))
            {
                _logger.LogWarning(
                    "Authorization failed for {Request}: missing role {Role}",
                    typeof(TRequest).Name, r.Role);
                return Task.FromResult(ResultHelper.MakeFailure<TResponse>(AuthorizationErrors.MissingRole(r.Role)));
            }
        }

        foreach (var p in _perms)
        {
            if (_user.Scope < p.MinScope)
            {
                _logger.LogWarning(
                    "Authorization failed for {Request}: scope {ActualScope} < required {RequiredScope} for {Permission}",
                    typeof(TRequest).Name, _user.Scope, p.MinScope, p.Permission);
                return Task.FromResult(ResultHelper.MakeFailure<TResponse>(AuthorizationErrors.InsufficientScope(p.Permission)));
            }
        }

        return next();
    }
}
