using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.Errors;
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
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public InventoryItem? InventoryItem { get; set; }

    public static InventoryReservation CreatePending(
        Guid id,
        Guid inventoryItemId,
        Guid productId,
        string orderIdempotencyKey,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow) =>
        new()
        {
            Id = id,
            InventoryItemId = inventoryItemId,
            ProductId = productId,
            OrderIdempotencyKey = orderIdempotencyKey,
            Quantity = quantity,
            Status = ReservationStatus.Pending,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

    /// <summary>
    /// Hard-deduct lifecycle: transitions PENDING → CONFIRMED. No-op when already CONFIRMED (safe under retry / concurrent xmin).
    /// </summary>
    public void Confirm(DateTimeOffset utcNow)
    {
        if (Status == ReservationStatus.Confirmed)
            return;

        if (Status != ReservationStatus.Pending)
            throw new InventoryDomainException(
                "InventoryReservation.InvalidStatus",
                $"Cannot confirm reservation in status {Status}");

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void MarkReleased(DateTimeOffset utcNow)
    {
        Status = ReservationStatus.Released;
        ReleasedAt = utcNow;
        UpdatedAt = utcNow;
    }
}
