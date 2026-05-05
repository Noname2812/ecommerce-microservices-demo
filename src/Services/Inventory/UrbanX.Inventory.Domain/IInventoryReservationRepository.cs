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

    Task<InventoryReservation?> GetTrackedByIdWithInventoryItemAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryReservation>> GetExpiredPendingBatchAsync(
        int batchSize,
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken);
}
