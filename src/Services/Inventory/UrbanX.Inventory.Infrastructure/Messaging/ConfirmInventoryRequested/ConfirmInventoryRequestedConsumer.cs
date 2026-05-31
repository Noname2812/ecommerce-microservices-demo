using MassTransit;
using MediatR;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Inventory.Infrastructure.Messaging.ConfirmInventoryRequested;

public sealed class ConfirmInventoryRequestedConsumer(ISender sender) : IConsumer<ConfirmInventoryRequestedV1>
{
    public Task Consume(ConsumeContext<ConfirmInventoryRequestedV1> context)
    {
        var command = new ConfirmReservationCommand(OrderId: context.Message.OrderId);
        return sender.Send(command, context.CancellationToken);
    }
}
