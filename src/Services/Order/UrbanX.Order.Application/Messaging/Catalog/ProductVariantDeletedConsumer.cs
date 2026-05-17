using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Messaging.Catalog;

public sealed class ProductVariantDeletedConsumer(
    ILogger<ProductVariantDeletedConsumer> logger,
    IUnitOfWork unitOfWork,
    IProcessedEventRepository processedEventRepository,
    ICatalogSnapshotWriter writer,
    IProductSnapshotCache cache)
    : CatalogProjectionConsumerBase<ProductUpdateIntegrationEvents.ProductVariantDeletedV1, ProductVariantDeletedConsumer>(
        logger, unitOfWork, processedEventRepository, writer, cache)
{
    protected override Task ProjectAsync(
        ProductUpdateIntegrationEvents.ProductVariantDeletedV1 @event,
        CancellationToken cancellationToken) =>
        Writer.DeleteVariantsAsync(new[] { @event.VariantId }, cancellationToken);

    protected override Task InvalidateCacheAsync(
        ProductUpdateIntegrationEvents.ProductVariantDeletedV1 @event,
        CancellationToken cancellationToken) =>
        Cache.InvalidateVariantAsync(@event.VariantId, cancellationToken);
}
