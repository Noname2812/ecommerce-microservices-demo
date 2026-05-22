using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

// Dispatched from ConfirmInventoryRequestedConsumer. IMessagingCommand skips TransactionPipelineBehavior
// because MassTransit EF Outbox already wraps the consumer in a DbContext transaction; double-wrapping
// causes "already in transaction" errors and breaks rollback semantics on Result.Failure.
[AllowAnonymous]
public record ConfirmReservationCommand(Guid OrderId) : IMessagingCommand;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
