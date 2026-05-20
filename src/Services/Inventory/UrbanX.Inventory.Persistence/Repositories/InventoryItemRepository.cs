using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class InventoryItemRepository(InventoryDbContext dbContext) : IInventoryItemRepository
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryItemIdsByProductAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        var distinct = productIds.Distinct().ToArray();

        // No row lock here — just an indexed lookup. The atomic UPDATE methods take the PG row
        // lock implicitly for ~ms during the statement itself.
        var rows = await dbContext.InventoryItems
            .AsNoTracking()
            .Where(i => distinct.Contains(i.ProductId))
            .Select(i => new { i.ProductId, i.Id })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First().Id);
    }

    public Task<int> TryReserveAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        return dbContext.InventoryItems
            .Where(i => i.Id == itemId
                        && i.QuantityOnHand - i.QuantityReserved >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.QuantityReserved, i => i.QuantityReserved + quantity)
                .SetProperty(i => i.UpdatedAt, _ => utcNow),
                cancellationToken);
    }

    public async Task<int> GetAvailableQuantityAsync(Guid itemId, CancellationToken cancellationToken)
    {
        return await dbContext.InventoryItems
            .AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.QuantityOnHand - i.QuantityReserved)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> DecrementReservedAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        return dbContext.InventoryItems
            .Where(i => i.Id == itemId && i.QuantityReserved >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.QuantityReserved, i => i.QuantityReserved - quantity)
                .SetProperty(i => i.UpdatedAt, _ => utcNow),
                cancellationToken);
    }

    public async Task<int?> ConfirmDeductAtomicAsync(
        Guid itemId,
        int quantity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // Raw SQL needed to RETURNING the pre-update quantity_on_hand (for the stock_movement audit row).
        // EF Core 10's ExecuteUpdateAsync does not yet expose RETURNING.
        var result = await dbContext.Database
            .SqlQuery<ConfirmDeductRow>(
                $@"UPDATE inventory_items
                   SET quantity_reserved = quantity_reserved - {quantity},
                       quantity_on_hand = quantity_on_hand - {quantity},
                       updated_at = {utcNow}
                   WHERE id = {itemId} AND quantity_reserved >= {quantity}
                   RETURNING (quantity_on_hand + {quantity}) AS ""QuantityOnHandBefore""")
            .ToListAsync(cancellationToken);

        return result.Count == 0 ? null : result[0].QuantityOnHandBefore;
    }

    private sealed record ConfirmDeductRow(int QuantityOnHandBefore);
}
