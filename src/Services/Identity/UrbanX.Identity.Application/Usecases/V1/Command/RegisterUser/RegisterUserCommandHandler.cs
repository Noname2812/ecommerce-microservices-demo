using Microsoft.AspNetCore.Identity;
using Shared.Application;
using Shared.Contract.Messaging.Identity;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Application.Usecases.V1.Errors;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Domain.ValueObjects;
using UrbanX.Identity.Infrastructure.Audit;
using UrbanX.Identity.Infrastructure.Email;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public sealed class RegisterUserCommandHandler : ICommandHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IIdentityAuditWriter _audit;
    private readonly IEventPublisher _eventPublisher;

    public RegisterUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IIdentityAuditWriter audit,
        IEventPublisher eventPublisher)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _audit = audit;
        _eventPublisher = eventPublisher;
    }

    public async Task<Result<RegisterUserResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _userManager.FindByEmailAsync(email) is not null)
            return Result.Failure<RegisterUserResponse>(AuthErrors.EmailAlreadyExists(email));

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PhoneNumber = request.PhoneNumber,
            EmailConfirmed = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var error = createResult.Errors.FirstOrDefault();
            return Result.Failure<RegisterUserResponse>(AuthErrors.WeakPassword(error?.Description ?? "Password does not meet requirements"));
        }

        await _userManager.AddToRoleAsync(user, Shared.Application.Authorization.Roles.Customer);

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailSender.SendEmailConfirmationAsync(user.Email!, user.DisplayName, BuildConfirmationLink(user.Id, token), cancellationToken);

        await _audit.WriteAsync(user.Id, user.Email, AuthEventType.Registered, new { method = "password" }, cancellationToken);

        await _eventPublisher.PublishAsync(new UserIntegrationEvents.UserRegisteredV1(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.PhoneNumber,
            user.MerchantId,
            new[] { Shared.Application.Authorization.Roles.Customer }
        ), cancellationToken);

        return Result.Success(new RegisterUserResponse(user.Id, user.Email!, RequiresEmailConfirmation: true));
    }

    private static string BuildConfirmationLink(Guid userId, string token) =>
        $"/api/account/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";
}
