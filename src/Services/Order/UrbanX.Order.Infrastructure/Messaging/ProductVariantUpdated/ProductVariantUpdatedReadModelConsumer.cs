using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantUpdated;

public sealed class ProductVariantUpdatedReadModelConsumer(
    ISender sender,
    IProductVariantReadModelRepository repository,
    ILogger<ProductVariantUpdatedReadModelConsumer> logger)
    : IConsumer<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1>
{
    public async Task Consume(ConsumeContext<ProductUpdateIntegrationEvents.ProductVariantUpdatedV1> context)
    {
        var msg = context.Message;
        var sibling = await repository.GetAnyByProductIdAsync(msg.ProductId, context.CancellationToken);

        if (sibling is null)
        {
            logger.LogWarning(
                "ProductVariantUpdated received for unknown product {ProductId}; skipping",
                msg.ProductId);
            return;
        }

        var variant = msg.Variant;
        var command = new RefreshProductVariantProjectionCommand(
            VariantId: variant.VariantId,
            ProductId: msg.ProductId,
            ProductName: sibling.ProductName,
            ProductIsActive: sibling.ProductIsActive,
            Sku: variant.Sku,
            VariantName: variant.Name,
            Price: variant.Price,
            IsActive: variant.IsActive,
            SellerId: msg.SellerId,
            SellerName: sibling.SellerName,
            SellerIsActive: sibling.SellerIsActive,
            ImageUrl: variant.ImageUrl,
            RowVersion: variant.RowVersion);

        await sender.Send(command, context.CancellationToken);

        logger.LogDebug(
            "Projected ProductVariantUpdated {VariantId} (RowVersion {RowVersion})",
            variant.VariantId, variant.RowVersion);
    }
}
