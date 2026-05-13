using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

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
        var productIds = merged.Keys.ToList();

        var lines = await _inventoryItems.GetTrackedPrimaryLinePerProductAsync(productIds, cancellationToken);

        foreach (var pid in productIds)
        {
            if (!lines.ContainsKey(pid))
                return Result.Failure<ReserveInventoryResponse>(
                    InventoryReservationErrors.ProductNotFoundForReservation(pid));
        }

        foreach (var (pid, qty) in merged)
        {
            var line = lines[pid];
            var available = line.QuantityAvailable;
            if (available < qty)
                return Result.Failure<ReserveInventoryResponse>(
                    InventoryReservationErrors.OutOfStock(pid, qty, available));
        }

        var utc = DateTimeOffset.UtcNow;
        var expiresAt = utc.AddMinutes(15);
        var newRows = new List<InventoryReservation>();

        foreach (var (pid, qty) in merged)
        {
            var item = lines[pid];
            item.QuantityReserved += qty;
            item.UpdatedAt = utc;

            newRows.Add(new InventoryReservation
            {
                Id = Guid.NewGuid(),
                InventoryItemId = item.Id,
                ProductId = pid,
                OrderIdempotencyKey = request.IdempotencyKey,
                Quantity = qty,
                Status = ReservationStatus.Pending,
                ExpiresAt = expiresAt,
                CreatedAt = utc,
                UpdatedAt = utc
            });
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
