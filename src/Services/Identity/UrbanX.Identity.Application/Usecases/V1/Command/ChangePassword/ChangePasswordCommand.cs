using FluentValidation;
using Shared.Application;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Matches("[A-Z]")
            .Matches("[a-z]")
            .Matches("[0-9]")
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must differ from current password");
    }
}
