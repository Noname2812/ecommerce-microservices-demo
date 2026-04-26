using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserContext _userContext;
    private readonly IIdentityAuditWriter _audit;

    public ChangePasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IUserContext userContext,
        IIdentityAuditWriter audit)
    {
        _userManager = userManager;
        _userContext = userContext;
        _audit = audit;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is null)
            return Result.Failure(AuthErrors.InvalidCredentials);

        var user = await _userManager.FindByIdAsync(_userContext.UserId.Value.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(_userContext.UserId.Value));

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var first = result.Errors.FirstOrDefault();
            return Result.Failure(string.Equals(first?.Code, "PasswordMismatch", StringComparison.OrdinalIgnoreCase)
                ? AuthErrors.CurrentPasswordIncorrect
                : AuthErrors.WeakPassword(first?.Description ?? "Password change failed"));
        }

        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.PasswordChanged, null, cancellationToken);
        return Result.Success();
    }
}
