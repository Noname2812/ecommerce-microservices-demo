using MassTransit;
using MediatR;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Inventory.Application.Usecases.V1.Command.Release;

namespace UrbanX.Inventory.Infrastructure.Messaging.InventoryReleaseRequested;

public sealed class InventoryReleaseRequestedConsumer(ISender sender) : IConsumer<InventoryReleaseRequestedV1>
{
    public Task Consume(ConsumeContext<InventoryReleaseRequestedV1> context)
    {
        var command = new ReleaseInventoryCommand(OrderId: context.Message.OrderId);
        return sender.Send(command, context.CancellationToken);
    }
}
