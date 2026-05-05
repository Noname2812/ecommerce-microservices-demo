using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class ProcessedEventRepository(PromotionDbContext dbContext) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        dbContext.ProcessedEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, cancellationToken);

    public void StageInsert(ProcessedEvent processedEvent) =>
        dbContext.ProcessedEvents.Add(processedEvent);
}
