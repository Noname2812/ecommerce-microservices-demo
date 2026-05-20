using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Domain;

public interface IInventoryReservationRepository
{
    /// <summary>
    /// Rows for this order idempotency key that still represent an active reservation outcome
    /// (pending hold or already confirmed). Excludes released/cancelled so a later lifecycle does not
    /// cause a duplicate reserve for the same key.
    /// </summary>
    Task<IReadOnlyList<InventoryReservation>> GetReservationsForIdempotentReplayAsync(
        string orderIdempotencyKey,
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
    /// Atomic <c>UPDATE … WHERE status = 'PENDING' RETURNING inventory_item_id, quantity, order_id</c>.
    /// Returns <c>null</c> when no row matches — caller falls back to <see cref="GetStatusAsync"/>
    /// to distinguish NotFound vs already Confirmed/Released/Cancelled.
    /// </summary>
    Task<ReservationConfirmResult?> TryMarkConfirmedAtomicAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads <c>status</c> column only; used to map <see cref="TryMarkReleasedAtomicAsync"/> /
    /// <see cref="TryMarkConfirmedAtomicAsync"/> miss into the correct domain error.
    /// </summary>
    Task<string?> GetStatusAsync(Guid reservationId, CancellationToken cancellationToken);
}

/// <summary>Projected result of an atomic release UPDATE … RETURNING.</summary>
public sealed record ReservationStateChangeResult(Guid InventoryItemId, int Quantity);

/// <summary>Projected result of an atomic confirm UPDATE … RETURNING (carries OrderId for the audit row).</summary>
public sealed record ReservationConfirmResult(Guid InventoryItemId, int Quantity, Guid? OrderId);
