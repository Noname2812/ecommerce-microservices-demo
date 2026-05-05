using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Domain;

public interface IInventoryItemRepository
{
    /// <summary>
    /// One tracked row per distinct product id (lowest <see cref="InventoryItem.Id"/> wins when multiple variants exist).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, InventoryItem>> GetTrackedPrimaryLinePerProductAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}
