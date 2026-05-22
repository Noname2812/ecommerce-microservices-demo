using Shared.Kernel.Domain;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Domain.Models;

/// <summary>Stock hold for an order line; ties reserved quantity on an <see cref="InventoryItem"/> to an idempotency key.</summary>
public class InventoryReservation : BaseEntity<Guid>
{
    /// <summary>Inventory item whose reserved quantity this row represents.</summary>
    public Guid InventoryItemId { get; set; }

    /// <summary>Order that owns this reservation; set when the order is known.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Number of units reserved for this row.</summary>
    public int Quantity { get; set; }

    /// <summary>Lifecycle state; see <see cref="ReservationStatus"/>.</summary>
    public string Status { get; set; } = ReservationStatus.Pending;

    /// <summary>UTC time after which an unconfirmed reservation may be expired or released.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>UTC time when this reservation row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time of the last status or metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time when stock was released back to available; null until released.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>UTC time when the reservation was confirmed (hard deduct); null while pending.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Parent inventory item navigation property.</summary>
    public InventoryItem? InventoryItem { get; set; }

    public static InventoryReservation CreatePending(
        Guid id,
        Guid inventoryItemId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow) =>
        new()
        {
            Id = id,
            InventoryItemId = inventoryItemId,
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
            // throw new InventoryDomainException(
            //     "InventoryReservation.InvalidStatus",
            //     $"Cannot confirm reservation in status {Status}");

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>Transitions status to <see cref="ReservationStatus.Released"/> and records <see cref="ReleasedAt"/>.</summary>
    public void MarkReleased(DateTimeOffset utcNow)
    {
        Status = ReservationStatus.Released;
        ReleasedAt = utcNow;
        UpdatedAt = utcNow;
    }
}
