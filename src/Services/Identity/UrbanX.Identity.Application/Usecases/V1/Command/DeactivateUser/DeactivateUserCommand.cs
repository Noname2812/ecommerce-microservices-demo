using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Users.Write, MinScope = PermissionScope.All)]
public record DeactivateUserCommand(Guid UserId, string? Reason) : ICommand;

public sealed class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
