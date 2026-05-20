using MediatR;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Usecases.V1.Command.Release;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class InventoryReleaseRequestedProcessor(IMediator mediator)
{
    public async Task ProcessAsync(InventoryReleaseRequestedV1 @event, CancellationToken cancellationToken)
    {
        var eventId = @event.EventId;

        var result = await mediator.Send(
            new ReleaseInventoryCommand(@event.ReservationId, eventId),
            cancellationToken);

        if (result.IsFailure)
            throw new InventoryReleaseCommandFailedException(result.Error);
    }
}
