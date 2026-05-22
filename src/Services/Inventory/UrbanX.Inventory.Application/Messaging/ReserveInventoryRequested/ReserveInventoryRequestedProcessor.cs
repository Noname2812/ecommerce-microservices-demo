using MediatR;
using MassTransit;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ReserveInventoryRequestedProcessor(ISender sender, IPublishEndpoint publishEndpoint)
{
    public async Task ProcessAsync(ReserveInventoryRequestedV1 evt, CancellationToken ct)
    {
        try
        {
            var command = new ReserveInventoryCommand(
                OrderId: evt.OrderId,
                ExpiresInMinutes: evt.ExpiresInMinutes,
                Items: evt.Items
                    .Select(i => new ReserveInventoryLineItem(i.VariantId, i.Quantity))
                    .ToList());

            var result = await sender.Send(command, ct);

            if (result.IsSuccess)
            {
                // await publishEndpoint.Publish(new InventoryReservedV1
                // {

                // }, ct);
            }
            else
            {

                // await publishEndpoint.Publish(new InventoryReserveFailedV1
                // {
                //     OrderId = evt.OrderId,
                //     CorrelationId = evt.OrderId.ToString("D"),
                //     CausationId = evt.EventId.ToString("D"),
                //     ErrorCode = result.Error.Code,
                //     ErrorMessage = result.Error.Message,
                // }, ct);
            }
        }
        // Catch OutOfStockException => publish InventoryReserveFailed
        catch (Exception)
        {

            throw;
        }


    }
}
