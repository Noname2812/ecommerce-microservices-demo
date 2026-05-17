using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

public sealed class ProductStatusChangedConsumer(
    ILogger<ProductStatusChangedConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : CatalogProjectionConsumerBase<ProductUpdateIntegrationEvents.ProductStatusChangedV1, ProductStatusChangedConsumer>(
        logger, unitOfWork, processedEventRepository, writer, cache)
{
    protected override Task ProjectAsync(
        ProductUpdateIntegrationEvents.ProductStatusChangedV1 @event,
        CancellationToken cancellationToken)
    {
        var version = VersionFrom(@event.OccurredOn);
        var updatedAt = @event.OccurredOn.UtcDateTime;
        var productActive = IsProductActive(@event.NewStatus);

        return Writer.UpdateProductStatusAsync(
            @event.ProductId,
            productActive,
            @event.AffectedVariantIds,
            version,
            updatedAt,
            cancellationToken);
    }

    protected override async Task InvalidateCacheAsync(
        ProductUpdateIntegrationEvents.ProductStatusChangedV1 @event,
        CancellationToken cancellationToken)
    {
        await Cache.InvalidateProductAsync(@event.ProductId, cancellationToken);
        foreach (var variantId in @event.AffectedVariantIds)
            await Cache.InvalidateVariantAsync(variantId, cancellationToken);
    }
}
