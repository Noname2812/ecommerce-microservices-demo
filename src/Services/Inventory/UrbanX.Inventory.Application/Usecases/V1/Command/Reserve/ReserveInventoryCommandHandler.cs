using Shared.Application;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

public sealed class ReserveInventoryCommandHandler(
    IInventoryItemRepository inventoryItems,
    IInventoryReservationRepository reservations,
    IEventPublisher eventPublisher) : ICommandHandler<ReserveInventoryCommand>
{
    public async Task<Result> Handle(ReserveInventoryCommand request, CancellationToken cancellationToken)
    {
        var existing = await reservations.GetActiveReservationsByOrderIdAsync(request.OrderId, cancellationToken);
        if (existing.Count > 0)
            return Result.Success();

        var variantIds = request.Items.Select(i => i.VariantId).Distinct().ToArray();
        var itemIdsByVariant = await inventoryItems.GetItemIdsByVariantIdsAsync(variantIds, cancellationToken);

        var unknownVariants = variantIds.Where(v => !itemIdsByVariant.ContainsKey(v)).ToList();
        if (unknownVariants.Count > 0)
        {
            await eventPublisher.PublishAsync(
                new InventoryReserveFailedV1
                {
                    OrderId = request.OrderId,
                    ErrorCode = "InventoryItem.NotFound",
                    ErrorMessage = "One or more variants are not stocked.",
                    VariantIdsOutOfStock = unknownVariants
                },
                cancellationToken);

            return Result.Failure(new Error("InventoryItem.NotFound", "Variant not found in inventory."));
        }

        var utcNow = DateTimeOffset.UtcNow;
        var expiresAt = utcNow.AddMinutes(request.ExpiresInMinutes);
        var newRows = new List<InventoryReservation>();
        var outOfStock = new List<Guid>();

        foreach (var line in request.Items.OrderBy(i => itemIdsByVariant[i.VariantId]))
        {
            var itemId = itemIdsByVariant[line.VariantId];
            var affected = await inventoryItems.TryReserveAtomicAsync(
                itemId,
                line.Quantity,
                utcNow,
                cancellationToken);

            if (affected == 0)
            {
                outOfStock.Add(line.VariantId);
                continue;
            }

            if (outOfStock.Count > 0)
            {
                continue;
            }
            
            var reservation = InventoryReservation.CreatePending(
                Guid.NewGuid(),
                itemId,
                line.Quantity,
                expiresAt,
                utcNow);
            reservation.OrderId = request.OrderId;
            newRows.Add(reservation);
        }

        if (outOfStock.Count > 0)
        {
            await eventPublisher.PublishAsync(
                new InventoryReserveFailedV1
                {
                    OrderId = request.OrderId,
                    ErrorCode = "InventoryItem.OutOfStock",
                    ErrorMessage = "Insufficient stock for one or more variants.",
                    VariantIdsOutOfStock = outOfStock
                },
                cancellationToken);

            return Result.Failure(new Error("InventoryItem.OutOfStock", "Insufficient stock."));
        }

        reservations.AddRange(newRows);

        await eventPublisher.PublishAsync(
            new InventoryReservedV1
            {
                OrderId = request.OrderId,
                ReservationIds = newRows.Select(r => r.Id).ToList(),
                ExpiresAt = expiresAt
            },
            cancellationToken);

        return Result.Success();
    }
}
