using MediatR;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ConfirmInventoryRequestedProcessor(ISender sender)
{
    public async Task ProcessAsync(ConfirmInventoryRequestedV1 @event, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ConfirmReservationCommand(@event.ReservationId, @event.IdempotencyKey, @event.EventId),
            cancellationToken);

        if (result.IsFailure)
            throw new ConfirmInventoryCommandFailedException(result.Error);
    }
}
