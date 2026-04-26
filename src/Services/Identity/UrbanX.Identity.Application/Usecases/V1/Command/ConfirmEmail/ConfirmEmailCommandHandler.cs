using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class ConfirmEmailCommandHandler : ICommandHandler<ConfirmEmailCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityAuditWriter _audit;

    public ConfirmEmailCommandHandler(UserManager<ApplicationUser> userManager, IIdentityAuditWriter audit)
    {
        _userManager = userManager;
        _audit = audit;
    }

    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(request.UserId));

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Result.Failure(AuthErrors.InvalidConfirmationToken);

        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.EmailConfirmed, null, cancellationToken);
        return Result.Success();
    }
}
