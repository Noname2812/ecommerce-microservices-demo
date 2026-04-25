using Shared.Kernel.Domain;

namespace UrbanX.Inventory.Domain.Models;

public class StockMovement : BaseEntity<Guid>
{
    public Guid InventoryItemId { get; set; }
    public string MovementType { get; set; } = null!;
    public int QuantityChange { get; set; }    // Positive = nhập, Negative = xuất
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public string? ReferenceType { get; set; } // ORDER | PURCHASE_ORDER | MANUAL_ADJUSTMENT
    public Guid? ReferenceId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedById { get; set; }     // cross-service, không có FK
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public InventoryItem? InventoryItem { get; set; }
}
