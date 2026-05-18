using Shared.Kernel.Domain;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain.Errors;

namespace UrbanX.Inventory.Domain.Models;

/// <summary>Optimistic concurrency uses PostgreSQL xmin (mapped as EF shadow property), not app-managed RowVersion.</summary>
public class InventoryItem : BaseEntity<Guid>
{
    // Denormalized từ Catalog service — không có FK (cross-service boundary)
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public Guid VariantId { get; set; }
    public string VariantSku { get; set; } = null!;
    public string? VariantName { get; set; }

    public Guid? WarehouseId { get; set; }

    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    // GENERATED ALWAYS AS (quantity_on_hand - quantity_reserved) STORED
    public int QuantityAvailable { get; private set; }

    public int ReorderPoint { get; set; } = 10;
    public int ReorderQuantity { get; set; } = 50;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Warehouse? Warehouse { get; set; }
    public ICollection<InventoryReservation> Reservations { get; set; } = new List<InventoryReservation>();
    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();

    public Error? ReleaseReservedQuantity(int quantity, DateTimeOffset utcNow)
    {
        if (quantity <= 0)
            return InventoryStockErrors.InvalidReleaseQuantity;
        if (QuantityReserved < quantity)
            return InventoryStockErrors.InsufficientReservedForRelease(Id, quantity, QuantityReserved);

        QuantityReserved -= quantity;
        UpdatedAt = utcNow;
        return null;
    }

    /// <summary>
    /// Applies hard deduct: decreases <see cref="QuantityReserved"/> and <see cref="QuantityOnHand"/> by <paramref name="quantity"/>.
    /// </summary>
    public void ConfirmDeduction(int quantity, DateTimeOffset utcNow)
    {
        if (quantity <= 0)
            throw new InventoryDomainException(
                "InventoryItem.InvalidConfirmQuantity",
                "Confirm quantity must be positive");

        if (quantity > QuantityReserved)
            throw new InventoryDomainException(
                "InventoryItem.InsufficientReservedForConfirm",
                $"Cannot confirm {quantity}; only {QuantityReserved} reserved");

        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
        UpdatedAt = utcNow;
    }
}
