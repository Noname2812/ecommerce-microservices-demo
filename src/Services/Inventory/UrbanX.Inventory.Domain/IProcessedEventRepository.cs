using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Domain;

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a row for insert on the current persistence unit of work (no SaveChanges).
    /// Used so inbox writes commit with the same transaction as command side-effects.
    /// </summary>
    void StageInsert(ProcessedEvent processedEvent);
}
