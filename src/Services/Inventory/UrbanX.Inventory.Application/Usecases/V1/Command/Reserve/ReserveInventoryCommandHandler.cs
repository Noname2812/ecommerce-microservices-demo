using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

public sealed class ReserveInventoryCommandHandler : ICommandHandler<ReserveInventoryCommand, ReserveInventoryResponse>
{
    private readonly IInventoryItemRepository _inventoryItems;
    private readonly IInventoryReservationRepository _reservations;

    public ReserveInventoryCommandHandler(
        IInventoryItemRepository inventoryItems,
        IInventoryReservationRepository reservations)
    {
        _inventoryItems = inventoryItems;
        _reservations = reservations;
    }

    public async Task<Result<ReserveInventoryResponse>> Handle(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _reservations.GetReservationsForIdempotentReplayAsync(
            request.IdempotencyKey,
            cancellationToken);

        if (existing.Count > 0)
            return Result.Success(MapExisting(existing));

        var merged = MergeLines(request.Items);

        // Lookup primary item id per product (no lock — atomic UPDATE below does the locking).
        var itemMap = await _inventoryItems.GetPrimaryItemIdsByProductAsync(
            merged.Keys.ToArray(),
            cancellationToken);

        foreach (var pid in merged.Keys)
        {
            if (!itemMap.ContainsKey(pid))
                return Result.Failure<ReserveInventoryResponse>(
                    InventoryReservationErrors.ProductNotFoundForReservation(pid));
        }

        var utc = DateTimeOffset.UtcNow;
        var expiresAt = utc.AddMinutes(15);
        var newRows = new List<InventoryReservation>();

        // Sort by item id so concurrent multi-item reserves acquire locks in the same order →
        // PostgreSQL deadlock-safe. Each UPDATE is an atomic CAS: increments quantity_reserved only
        // when stock still available; PG row lock is held ~ms during the UPDATE, then released.
        var orderedItems = merged
            .Select(kv => new { ProductId = kv.Key, ItemId = itemMap[kv.Key], Qty = kv.Value })
            .OrderBy(x => x.ItemId)
            .ToList();

        foreach (var item in orderedItems)
        {
            var affected = await _inventoryItems.TryReserveAtomicAsync(
                item.ItemId, item.Qty, utc, cancellationToken);

            if (affected == 0)
            {
                // Failed CAS = stock exhausted at the moment of UPDATE. Read the current available
                // for a helpful error message; TransactionPipelineBehavior will roll back any earlier
                // UPDATEs we did in this same transaction.
                var available = await _inventoryItems.GetAvailableQuantityAsync(item.ItemId, cancellationToken);
                return Result.Failure<ReserveInventoryResponse>(
                    InventoryReservationErrors.OutOfStock(item.ProductId, item.Qty, available));
            }

            newRows.Add(InventoryReservation.CreatePending(
                id:                  Guid.NewGuid(),
                inventoryItemId:     item.ItemId,
                productId:           item.ProductId,
                orderIdempotencyKey: request.IdempotencyKey,
                quantity:            item.Qty,
                expiresAt:           expiresAt,
                utcNow:              utc));
        }

        _reservations.AddRange(newRows);

        var ordered = newRows.OrderBy(r => r.Id).ToList();
        var reservationId = ordered[0].Id;

        return Result.Success(
            new ReserveInventoryResponse(
                reservationId,
                expiresAt,
                ordered.Select(r => new ReservedItemResponse(r.ProductId, r.Quantity)).ToList()));
    }

    private static IReadOnlyDictionary<Guid, int> MergeLines(IReadOnlyList<ReserveInventoryLineItem> items)
    {
        var map = new Dictionary<Guid, int>();
        foreach (var line in items)
            map[line.ProductId] = map.GetValueOrDefault(line.ProductId) + line.Quantity;
        return map;
    }

    private static ReserveInventoryResponse MapExisting(IReadOnlyList<InventoryReservation> rows)
    {
        var ordered = rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
        var head = ordered[0];
        return new ReserveInventoryResponse(
            head.Id,
            head.ExpiresAt,
            ordered.Select(r => new ReservedItemResponse(r.ProductId, r.Quantity)).ToList());
    }
}
