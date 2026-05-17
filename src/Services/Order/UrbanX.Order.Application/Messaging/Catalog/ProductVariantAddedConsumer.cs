using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

public sealed class ProductVariantAddedConsumer(
    ILogger<ProductVariantAddedConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : CatalogProjectionConsumerBase<ProductUpdateIntegrationEvents.ProductVariantAddedV1, ProductVariantAddedConsumer>(
        logger, unitOfWork, processedEventRepository, writer, cache)
{
    protected override Task ProjectAsync(
        ProductUpdateIntegrationEvents.ProductVariantAddedV1 @event,
        CancellationToken cancellationToken)
    {
        var version = VersionFrom(@event.OccurredOn);
        var updatedAt = @event.OccurredOn.UtcDateTime;
        var v = @event.Variant;

        // ProductIsActive unknown from this event in isolation — assume true; ProductStatusChanged will correct if needed.
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
        ProductUpdateIntegrationEvents.ProductVariantAddedV1 @event,
        CancellationToken cancellationToken) =>
        Cache.InvalidateVariantAsync(@event.Variant.VariantId, cancellationToken);
}
