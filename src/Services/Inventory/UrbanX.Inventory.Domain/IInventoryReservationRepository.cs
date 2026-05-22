using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Domain;

public interface IInventoryReservationRepository
{
    /// <summary>
    /// Active reservations for an order (pending or confirmed). Used for reserve idempotency replay.
    /// </summary>
    Task<IReadOnlyList<InventoryReservation>> GetActiveReservationsByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    /// <summary>Pending reservation ids for an order (compensation / release by order).</summary>
    Task<IReadOnlyList<Guid>> GetPendingReservationIdsByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken);

    void AddRange(IEnumerable<InventoryReservation> reservations);

    /// <summary>
    /// Tracked + InventoryItem-joined lookup; used by the TTL expiry sweep that mutates entities
    /// through the domain model. Hot transactional paths (release/confirm) use the atomic-CAS
    /// helpers below to avoid xmin conflicts.
    /// </summary>
    Task<IReadOnlyList<InventoryReservation>> GetExpiredPendingBatchAsync(
        int batchSize,
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomic <c>UPDATE … WHERE status = 'PENDING' RETURNING inventory_item_id, quantity</c>.
    /// Returns <c>null</c> when no row matches — caller falls back to <see cref="GetStatusAsync"/>
    /// to distinguish NotFound vs already Released/Confirmed/Cancelled.
    /// </summary>
    Task<ReservationStateChangeResult?> TryMarkReleasedAtomicAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomic confirm for all pending rows on an order. Returns one entry per updated reservation.
    /// </summary>
    Task<IReadOnlyList<ReservationConfirmResult>> TryMarkConfirmedByOrderIdAsync(
        Guid orderId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>Reads status of the first reservation row for the order, if any.</summary>
    Task<string?> GetStatusByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>Projected result of an atomic release UPDATE … RETURNING.</summary>
public sealed record ReservationStateChangeResult(Guid InventoryItemId, int Quantity);

/// <summary>Projected result of an atomic confirm UPDATE … RETURNING (carries OrderId for the audit row).</summary>
public sealed record ReservationConfirmResult(Guid InventoryItemId, int Quantity, Guid? OrderId);
