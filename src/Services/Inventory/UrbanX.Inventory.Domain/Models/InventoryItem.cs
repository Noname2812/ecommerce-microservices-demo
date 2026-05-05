using Shared.Kernel.Domain;

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
}
