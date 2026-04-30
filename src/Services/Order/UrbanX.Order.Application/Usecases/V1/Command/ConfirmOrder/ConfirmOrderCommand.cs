using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Orders.Write, MinScope = PermissionScope.All)]
public record ConfirmOrderCommand(Guid OrderId) : ICommand;

public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    public ConfirmOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
