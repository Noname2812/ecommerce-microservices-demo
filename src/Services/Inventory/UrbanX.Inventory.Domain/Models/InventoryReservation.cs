using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Domain.Models;

public class InventoryReservation : BaseEntity<Guid>
{
    public Guid InventoryItemId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = ReservationStatus.Reserved;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public InventoryItem? InventoryItem { get; set; }
}
