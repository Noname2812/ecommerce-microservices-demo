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
}
