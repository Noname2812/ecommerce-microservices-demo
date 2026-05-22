using MassTransit;
using MediatR;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Inventory.Infrastructure.Messaging.ReserveInventoryRequested;

public sealed class ReserveInventoryRequestedConsumer(ISender sender) : IConsumer<ReserveInventoryRequestedV1>
{
    public Task Consume(ConsumeContext<ReserveInventoryRequestedV1> context)
    {
        var command = new ReserveInventoryCommand(
            OrderId: context.Message.OrderId,
            ExpiresInMinutes: context.Message.ExpiresInMinutes,
            Items: context.Message.Items
                .Select(i => new ReserveInventoryLineItem(i.VariantId, i.Quantity))
                .ToList());

        return sender.Send(command, context.CancellationToken);
    }
}
