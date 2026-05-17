using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

public sealed class ProductVariantUpdatedConsumer(
    ILogger<ProductVariantUpdatedConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : CatalogProjectionConsumerBase<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1, ProductVariantUpdatedConsumer>(
        logger, unitOfWork, processedEventRepository, writer, cache)
{
    protected override Task ProjectAsync(
        ProductUpdateIntegrationEvents.ProductVariantUpdatedV1 @event,
        CancellationToken cancellationToken)
    {
        var version = VersionFrom(@event.OccurredOn);
        var updatedAt = @event.OccurredOn.UtcDateTime;
        var v = @event.Variant;

        // We don't know the latest product status from this event — keep existing ProductIsActive via upsert path
        // by passing true here. The ON CONFLICT UPDATE in the writer overwrites only when version is newer;
        // ProductStatusChanged events are the authoritative source for ProductIsActive flips.
        var row = new CatalogSnapshotRow(
            v.VariantId,
            @event.ProductId,
            v.Sku,
            ProductIsActive: true,
            v.IsActive,
            v.Price,
            version,
            updatedAt);

        return Writer.UpsertVariantsAsync(new[] { row }, cancellationToken);
    }

    protected override Task InvalidateCacheAsync(
        ProductUpdateIntegrationEvents.ProductVariantUpdatedV1 @event,
        CancellationToken cancellationToken) =>
        Cache.InvalidateVariantAsync(@event.VariantId, cancellationToken);
}
