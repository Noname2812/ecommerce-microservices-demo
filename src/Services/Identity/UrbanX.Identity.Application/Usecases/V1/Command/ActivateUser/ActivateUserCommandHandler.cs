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

public sealed class ActivateUserCommandHandler : ICommandHandler<ActivateUserCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserContext _userContext;
    private readonly IIdentityAuditWriter _audit;
    private readonly IOutboxWriter _outbox;

    public ActivateUserCommandHandler(
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

    public async Task<Result> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(request.UserId));

        user.IsActive = true;
        user.DeactivatedAt = null;
        user.DeactivationReason = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Result.Failure(new Error("User.ActivateFailed", result.Errors.FirstOrDefault()?.Description ?? "Failed to activate user"));

        await _audit.WriteAsync(
            user.Id,
            user.Email,
            AuthEventType.AccountActivated,
            new { by = _userContext.UserId },
            cancellationToken);

        await _outbox.WriteAsync(new UserIntegrationEvents.UserActivatedV1(
            user.Id, user.Email!, _userContext.UserId
        ), cancellationToken);

        return Result.Success();
    }
}
