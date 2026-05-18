using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

[AllowAnonymous]
public record ConfirmReservationCommand(
    Guid ReservationId,
    string IdempotencyKey,
    Guid? EventId = null)
    : ICommand, IConcurrencyRetriableCommand;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EventId!.Value)
            .NotEqual(Guid.Empty)
            .When(x => x.EventId.HasValue);
    }
}
