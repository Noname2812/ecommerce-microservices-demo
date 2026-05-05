using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Domain.Models;

public class InventoryReservation : BaseEntity<Guid>
{
    public Guid InventoryItemId { get; set; }

    /// <summary>Denormalized from inventory item / catalog for queries and idempotency.</summary>
    public Guid ProductId { get; set; }

    public Guid? OrderId { get; set; }
    public Guid? OrderItemId { get; set; }

    /// <summary>Client-supplied key; multiple rows may share one key (multi-line reservations).</summary>
    public string OrderIdempotencyKey { get; set; } = null!;

    public int Quantity { get; set; }
    public string Status { get; set; } = ReservationStatus.Pending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }

    public InventoryItem? InventoryItem { get; set; }
}
