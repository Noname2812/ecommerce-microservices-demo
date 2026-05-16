using MediatR;
using MassTransit;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;
using UrbanX.Inventory.Domain.Errors;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ReserveInventoryRequestedProcessor(ISender sender, IPublishEndpoint publishEndpoint)
{
    public async Task ProcessAsync(ReserveInventoryRequestedV1 evt, CancellationToken ct)
    {
        var command = new ReserveInventoryCommand(
            IdempotencyKey: evt.OrderIdempotencyKey,
            Items: evt.Items
                .Select(i => new ReserveInventoryLineItem(i.ProductId, i.Quantity))
                .ToList());

        var result = await sender.Send(command, ct);

        if (result.IsSuccess)
        {
            var value = result.Value!;
            var variantById = evt.Items.ToDictionary(i => i.ProductId, i => i.VariantId);

            await publishEndpoint.Publish(new InventoryReservedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = evt.EventId.ToString("D"),
                ReservationId = value.ReservationId,
                ExpiresAt = value.ExpiresAt,
                Items = value.Items
                    .Select(i => new InventoryReserveItem(
                        i.ProductId,
                        variantById.GetValueOrDefault(i.ProductId),
                        i.Quantity))
                    .ToList()
            }, ct);
        }
        else
        {
            var outOfStock = result.Error is OutOfStockError oos
                ? (IReadOnlyList<OutOfStockProduct>)[new OutOfStockProduct(oos.ProductId, oos.Available)]
                : [];

            await publishEndpoint.Publish(new InventoryReserveFailedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = evt.EventId.ToString("D"),
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
                OutOfStockProducts = outOfStock
            }, ct);
        }
    }
}
