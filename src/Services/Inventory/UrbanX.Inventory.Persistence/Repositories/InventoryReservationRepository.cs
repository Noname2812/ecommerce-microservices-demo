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

    public async Task<InventoryReservation?> GetTrackedByIdWithInventoryItemAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.InventoryReservations
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<InventoryReservation?> GetTrackedByIdWithInventoryItemForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        // Single query: FOR UPDATE inside the FromSql subquery locks the reservation row;
        // EF joins inventory_items into the same SELECT (item lock is xmin optimistic, not row-level).
        // Note: SELECT * maps by column name; new reservation columns require a migration before deploy.
        return await dbContext.InventoryReservations
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM inventory_reservations
                 WHERE id = {id}
                 FOR UPDATE
                 """)
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(cancellationToken);
    }

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
}
