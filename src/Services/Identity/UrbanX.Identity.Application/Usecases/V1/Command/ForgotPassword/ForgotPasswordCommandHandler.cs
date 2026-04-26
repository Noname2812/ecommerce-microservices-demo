using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;
using UrbanX.Identity.Infrastructure.Email;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IIdentityAuditWriter _audit;

    public ForgotPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IIdentityAuditWriter audit)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _audit = audit;
    }

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);

        // Intentionally do not leak whether the email exists
        if (user is null || !user.IsActive)
            return Result.Success();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"/api/account/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        await _emailSender.SendPasswordResetAsync(user.Email!, user.DisplayName, link, cancellationToken);
        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.PasswordResetRequested, null, cancellationToken);

        return Result.Success();
    }
}
