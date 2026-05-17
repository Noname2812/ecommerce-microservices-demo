using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Persistence.Repositories;

internal sealed class ProcessedEventRepository(OrderDbContext dbContext) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        dbContext.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId, cancellationToken);

    public void StageInsert(ProcessedEvent processedEvent) =>
        dbContext.ProcessedEvents.Add(processedEvent);
}
