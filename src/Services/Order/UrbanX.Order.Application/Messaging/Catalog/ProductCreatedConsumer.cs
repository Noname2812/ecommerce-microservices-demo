using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

public sealed class ProductCreatedConsumer(
    ILogger<ProductCreatedConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : CatalogProjectionConsumerBase<ProductIntegrationEvents.ProductCreatedV1, ProductCreatedConsumer>(
        logger, unitOfWork, processedEventRepository, writer, cache)
{
    protected override Task ProjectAsync(
        ProductIntegrationEvents.ProductCreatedV1 @event,
        CancellationToken cancellationToken)
    {
        var version = VersionFrom(@event.OccurredOn);
        var updatedAt = @event.OccurredOn.UtcDateTime;
        var productActive = IsProductActive(@event.Status);

        var rows = @event.Variants
            .Select(v => new CatalogSnapshotRow(
                v.VariantId,
                @event.ProductId,
                v.Sku,
                productActive,
                v.IsActive,
                v.Price,
                version,
                updatedAt))
            .ToArray();

        return Writer.UpsertVariantsAsync(rows, cancellationToken);
    }

    protected override async Task InvalidateCacheAsync(
        ProductIntegrationEvents.ProductCreatedV1 @event,
        CancellationToken cancellationToken)
    {
        await Cache.InvalidateProductAsync(@event.ProductId, cancellationToken);
        foreach (var variant in @event.Variants)
            await Cache.InvalidateVariantAsync(variant.VariantId, cancellationToken);
    }
}
