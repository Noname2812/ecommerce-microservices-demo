using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Identity.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Users.ManageRoles, MinScope = PermissionScope.All)]
public record AssignRoleCommand(Guid UserId, string Role) : ICommand;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().MaximumLength(50);
    }
}
