using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

[AllowAnonymous]
public record RegisterUserCommand(
    string Email,
    string Password,
    string DisplayName,
    string? PhoneNumber
) : ICommand<RegisterUserResponse>;

public record RegisterUserResponse(Guid UserId, string Email, bool RequiresEmailConfirmation);

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(32)
            .Matches(@"^\+?[0-9\s\-]+$")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}
