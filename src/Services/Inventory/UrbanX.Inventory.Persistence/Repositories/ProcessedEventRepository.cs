using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class ProcessedEventRepository(InventoryDbContext dbContext) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        dbContext.ProcessedEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, cancellationToken);

    public void StageInsert(ProcessedEvent processedEvent) =>
        dbContext.ProcessedEvents.Add(processedEvent);
}
