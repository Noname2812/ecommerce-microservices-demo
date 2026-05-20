using Shared.Kernel.Domain;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain.Errors;

namespace UrbanX.Inventory.Domain.Models;

/// <summary>Optimistic concurrency uses PostgreSQL xmin (mapped as EF shadow property), not app-managed RowVersion.</summary>
public class InventoryItem : BaseEntity<Guid>
{
    /// <summary>Catalog product identifier (denormalized; no cross-service FK).</summary>
    public Guid ProductId { get; set; }

    /// <summary>Catalog product display name (denormalized).</summary>
    public string ProductName { get; set; } = null!;

    /// <summary>Catalog variant identifier (denormalized).</summary>
    public Guid VariantId { get; set; }

    /// <summary>Catalog variant SKU (denormalized).</summary>
    public string VariantSku { get; set; } = null!;

    /// <summary>Optional catalog variant display name (denormalized).</summary>
    public string? VariantName { get; set; }

    /// <summary>Warehouse that holds this stock; null when not assigned to a warehouse.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Physical units currently in stock.</summary>
    public int QuantityOnHand { get; set; }

    /// <summary>Units held for in-flight orders (not yet confirmed or released).</summary>
    public int QuantityReserved { get; set; }

    /// <summary>Units available to sell: <see cref="QuantityOnHand"/> minus <see cref="QuantityReserved"/> (PostgreSQL generated stored column).</summary>
    public int QuantityAvailable { get; private set; }

    /// <summary>Stock level at or below which a reorder should be triggered.</summary>
    public int ReorderPoint { get; set; } = 10;

    /// <summary>Suggested quantity to reorder when stock falls at or below <see cref="ReorderPoint"/>.</summary>
    public int ReorderQuantity { get; set; } = 50;

    /// <summary>UTC timestamp of the last stock or metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Warehouse navigation property.</summary>
    public Warehouse? Warehouse { get; set; }

    /// <summary>Active and historical reservations against this item.</summary>
    public ICollection<InventoryReservation> Reservations { get; set; } = new List<InventoryReservation>();

    /// <summary>Append-only audit trail of stock changes for this item.</summary>
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
