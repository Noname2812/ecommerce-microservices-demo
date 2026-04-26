using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Identity;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class RevokeRoleCommandHandler : ICommandHandler<RevokeRoleCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserContext _userContext;
    private readonly IIdentityAuditWriter _audit;
    private readonly IOutboxWriter _outbox;

    public RevokeRoleCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserContext userContext,
        IIdentityAuditWriter audit,
        IOutboxWriter outbox)
    {
        _userManager = userManager;
        _userContext = userContext;
        _audit = audit;
        _outbox = outbox;
    }

    public async Task<Result> Handle(RevokeRoleCommand request, CancellationToken cancellationToken)
    {
        if (_userContext.UserId == request.UserId)
            return Result.Failure(AuthErrors.CannotChangeOwnRole);

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(request.UserId));

        if (!await _userManager.IsInRoleAsync(user, request.Role))
            return Result.Failure(AuthErrors.UserNotInRole(request.Role));

        var result = await _userManager.RemoveFromRoleAsync(user, request.Role);
        if (!result.Succeeded)
            return Result.Failure(new Error("Role.RevokeFailed", result.Errors.FirstOrDefault()?.Description ?? "Failed to revoke role"));

        await _audit.WriteAsync(
            user.Id,
            user.Email,
            AuthEventType.RoleRevoked,
            new { role = request.Role, by = _userContext.UserId },
            cancellationToken);

        await _outbox.WriteAsync(new UserIntegrationEvents.UserRoleRevokedV1(
            user.Id, user.Email!, request.Role, _userContext.UserId
        ), cancellationToken);

        return Result.Success();
    }
}
