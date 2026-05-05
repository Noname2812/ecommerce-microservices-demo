using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class InventoryItemRepository(InventoryDbContext dbContext) : IInventoryItemRepository
{
    public async Task<IReadOnlyDictionary<Guid, InventoryItem>> GetTrackedPrimaryLinePerProductAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return new Dictionary<Guid, InventoryItem>();

        var distinct = productIds.Distinct().ToList();
        var rows = await dbContext.InventoryItems
            .Where(i => distinct.Contains(i.ProductId))
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First());
    }
}
