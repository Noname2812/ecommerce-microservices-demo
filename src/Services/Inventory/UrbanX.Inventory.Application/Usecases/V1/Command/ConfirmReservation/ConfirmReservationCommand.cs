using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

[AllowAnonymous]
public record ConfirmReservationCommand(Guid OrderId) : ICommand;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
