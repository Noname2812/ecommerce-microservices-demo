using Microsoft.Extensions.Logging;
using Shared.Contract.Abstractions;
using Shared.Kernel.Primitives;
using Shared.Messaging;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

/// <summary>
/// Common projection-consumer wiring: inbox dedup, transactional upsert, post-commit cache invalidation.
/// Subclasses implement <see cref="ProjectAsync"/> (DB mutation) and <see cref="InvalidateCacheAsync"/> (best-effort cache eviction).
/// </summary>
public abstract class CatalogProjectionConsumerBase<TEvent, TConsumer>(
    ILogger<TConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : IntegrationEventConsumerBase<TEvent, TConsumer>(logger)
    where TEvent : class, IIntegrationEvent
{
    protected ICatalogSnapshotWriter Writer { get; } = writer;
    protected IProductSnapshotCache Cache { get; } = cache;

    protected static bool IsProductActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    protected static long VersionFrom(DateTimeOffset occurredOn) =>
        occurredOn.UtcTicks;

    protected sealed override async Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
    {
        if (await processedEventRepository.ExistsAsync(@event.EventId, cancellationToken))
            return;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await ProjectAsync(@event, cancellationToken);

            processedEventRepository.StageInsert(new ProcessedEvent
            {
                EventId = @event.EventId,
                EventType = typeof(TEvent).Name,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }, cancellationToken);

        await InvalidateCacheAsync(@event, cancellationToken);
    }

    protected abstract Task ProjectAsync(TEvent @event, CancellationToken cancellationToken);

    protected virtual Task InvalidateCacheAsync(TEvent @event, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
