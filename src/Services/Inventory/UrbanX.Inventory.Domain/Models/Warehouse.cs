using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Domain.Models;

public class Warehouse : BaseEntity<Guid>
{
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public WarehouseAddress Address { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
}
