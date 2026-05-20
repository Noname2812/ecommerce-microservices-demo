using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class InventoryReservationRepository(InventoryDbContext dbContext) : IInventoryReservationRepository
{
    public async Task<IReadOnlyList<InventoryReservation>> GetReservationsForIdempotentReplayAsync(
        string orderIdempotencyKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.InventoryReservations
            .AsNoTracking()
            .Where(r =>
                r.OrderIdempotencyKey == orderIdempotencyKey &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public void AddRange(IEnumerable<InventoryReservation> reservations) =>
        dbContext.InventoryReservations.AddRange(reservations);

    public async Task<IReadOnlyList<InventoryReservation>> GetExpiredPendingBatchAsync(
        int batchSize,
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken)
    {
        return await dbContext.InventoryReservations
            .Include(r => r.InventoryItem)
            .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAt < expiredBefore)
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReservationStateChangeResult?> TryMarkReleasedAtomicAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQuery<ReservationStateChangeResult>(
                $@"UPDATE inventory_reservations
                   SET status = {ReservationStatus.Released},
                       updated_at = {utcNow},
                       released_at = {utcNow}
                   WHERE id = {reservationId} AND status = {ReservationStatus.Pending}
                   RETURNING inventory_item_id AS ""InventoryItemId"", quantity AS ""Quantity""")
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<ReservationConfirmResult?> TryMarkConfirmedAtomicAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQuery<ReservationConfirmResult>(
                $@"UPDATE inventory_reservations
                   SET status = {ReservationStatus.Confirmed},
                       updated_at = {utcNow},
                       confirmed_at = {utcNow}
                   WHERE id = {reservationId} AND status = {ReservationStatus.Pending}
                   RETURNING inventory_item_id AS ""InventoryItemId"",
                             quantity AS ""Quantity"",
                             order_id AS ""OrderId""")
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<string?> GetStatusAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        return await dbContext.InventoryReservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => r.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
