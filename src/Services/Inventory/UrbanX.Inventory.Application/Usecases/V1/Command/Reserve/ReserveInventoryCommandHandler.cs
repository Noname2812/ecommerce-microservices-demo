using MassTransit;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

public sealed class ReserveInventoryCommandHandler : ICommandHandler<ReserveInventoryCommand>
{
    private readonly IInventoryItemRepository _inventoryItems;
    private readonly IInventoryReservationRepository _reservations;
    private readonly IPublishEndpoint _publishEndpoint;

    public ReserveInventoryCommandHandler(
        IPublishEndpoint publishEndpoint,
        IInventoryItemRepository inventoryItems,
        IInventoryReservationRepository reservations)
    {
        _publishEndpoint = publishEndpoint;
        _inventoryItems = inventoryItems;
        _reservations = reservations;
    }

    public async Task<Result> Handle(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Check VariantIds valid.
            // If invalid => publish event InventoryReserveFailed
        
        // 2. Create reservations with status=PENDING and expires_at=utc+request.ExpiresInMinutes.
        var utc = DateTimeOffset.UtcNow;
        var expiresAt = utc.AddMinutes(request.ExpiresInMinutes);
        var newRows = new List<InventoryReservation>();
        var variantIdsOutOfStock = new List<Guid>();

        foreach (var item in request.Items)
        {
            var affected = await _inventoryItems.TryReserveAtomicAsync(
                item.VariantId, item.Quantity, utc, cancellationToken);

            if (affected == 0)
            {
                // Add to out-of-stock list for error message, but keep trying to reserve other items to return a more complete result to client.
                variantIdsOutOfStock.Add(item.VariantId);
                continue;
            }

            if (variantIdsOutOfStock.Count > 0)
            {
                continue; // Don't create reservations if any items are out of stock, but keep trying to reserve other items to return a more complete result to client.
            }

            // Create reservation rows for this item.
        }

        if (variantIdsOutOfStock.Count > 0)
        {
            // publish event Out of stock
            // return Failure
        }

        _reservations.AddRange(newRows);

        // publish event InventoryReserved
        return Result.Success();
    }
}
