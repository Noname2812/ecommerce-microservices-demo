using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityAuditWriter _audit;

    public ResetPasswordCommandHandler(UserManager<ApplicationUser> userManager, IIdentityAuditWriter audit)
    {
        _userManager = userManager;
        _audit = audit;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
            return Result.Failure(AuthErrors.InvalidPasswordResetToken);

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var first = result.Errors.FirstOrDefault();
            return Result.Failure(string.Equals(first?.Code, "InvalidToken", StringComparison.OrdinalIgnoreCase)
                ? AuthErrors.InvalidPasswordResetToken
                : AuthErrors.WeakPassword(first?.Description ?? "Password reset failed"));
        }

        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.PasswordReset, null, cancellationToken);
        return Result.Success();
    }
}
