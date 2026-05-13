using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Application.DependencyInjection.Options;

namespace UrbanX.Inventory.Application.Jobs;

public sealed class ReleaseExpiredReservationsJob(
    IInventoryReservationRepository reservationRepository,
    IUnitOfWork unitOfWork,
    IOptions<ReleaseExpiredReservationsJobOptions> options,
    ILogger<ReleaseExpiredReservationsJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = $"ttl-job-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var utcNow = DateTimeOffset.UtcNow;
        var releasedCount = 0;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var batch = await reservationRepository.GetExpiredPendingBatchAsync(
                options.Value.BatchSize, utcNow, cancellationToken);

            if (batch.Count == 0)
                return;

            foreach (var reservation in batch)
            {
                if (reservation.InventoryItem is null)
                    continue;

                reservation.MarkReleased(utcNow);
                reservation.InventoryItem.ReleaseReservedQuantity(reservation.Quantity, utcNow);
                releasedCount++;
            }
        });

        if (releasedCount > 0)
            logger.LogInformation(
                "[{CorrelationId}] Released {Count} expired reservations",
                correlationId, releasedCount);
    }
}
