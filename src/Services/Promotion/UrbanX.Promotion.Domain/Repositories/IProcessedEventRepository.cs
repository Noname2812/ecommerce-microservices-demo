using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a row for insert on the current unit of work (no SaveChanges).
    /// Used so inbox writes commit with the same transaction as command side-effects.
    /// </summary>
    void StageInsert(ProcessedEvent processedEvent);
}
