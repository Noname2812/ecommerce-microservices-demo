using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Domain.Repositories;

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a row for insert on the current persistence unit of work (no SaveChanges).
    /// Used so inbox writes commit with the same transaction as projection side-effects.
    /// </summary>
    void StageInsert(ProcessedEvent processedEvent);
}
