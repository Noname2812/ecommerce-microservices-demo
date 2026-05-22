using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

// Atomic CAS UPDATEs eliminate xmin conflicts on inventory_items, but the processed_events PK can
// still raise PostgreSQL 23505 under concurrent broker redelivery. IConcurrencyRetriableCommand gives
// EfUnitOfWork bounded retry; on the second attempt the inbox lookup short-circuits with Success.
[AllowAnonymous]
public record ConfirmReservationCommand(
    Guid OrderId,
    Guid? EventId = null)
    : ICommand;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.EventId!.Value)
            .NotEqual(Guid.Empty)
            .When(x => x.EventId.HasValue);
    }
}
