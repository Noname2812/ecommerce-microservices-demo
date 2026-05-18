using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.ValueObjects;

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

    public static StockMovement CreateSale(
        Guid inventoryItemId,
        int quantity,
        string referenceType,
        Guid? referenceId,
        string note,
        Guid? createdById,
        string? createdByName,
        DateTimeOffset utcNow,
        int quantityOnHandBefore)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            InventoryItemId = inventoryItemId,
            MovementType = ValueObjects.MovementType.Sale,
            QuantityChange = -quantity,
            QuantityBefore = quantityOnHandBefore,
            QuantityAfter = quantityOnHandBefore - quantity,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Note = note,
            CreatedById = createdById,
            CreatedByName = createdByName,
            CreatedAt = utcNow
        };
    }
}
