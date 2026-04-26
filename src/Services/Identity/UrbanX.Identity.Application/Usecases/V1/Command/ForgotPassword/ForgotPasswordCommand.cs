using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

[AllowAnonymous]
public record ForgotPasswordCommand(string Email) : ICommand;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
