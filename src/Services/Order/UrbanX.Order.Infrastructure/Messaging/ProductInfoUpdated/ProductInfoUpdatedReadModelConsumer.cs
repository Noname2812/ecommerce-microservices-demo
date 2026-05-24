using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductInfoUpdated;

public sealed class ProductInfoUpdatedReadModelConsumer(
    ISender sender,
    ILogger<ProductInfoUpdatedReadModelConsumer> logger)
    : IConsumer<ProductUpdateIntegrationEvents.ProductInfoUpdatedV1>
{
    public async Task Consume(ConsumeContext<ProductUpdateIntegrationEvents.ProductInfoUpdatedV1> context)
    {
        var msg = context.Message;
        var productIsActive = string.Equals(msg.Snapshot.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var sellerName = msg.Snapshot.SellerName ?? string.Empty;

        foreach (var variant in msg.ActiveVariants)
        {
            var command = new RefreshProductVariantProjectionCommand(
                VariantId: variant.VariantId,
                ProductId: msg.ProductId,
                ProductName: msg.Snapshot.Name,
                ProductIsActive: productIsActive,
                Sku: variant.Sku,
                VariantName: variant.Name,
                Price: variant.Price,
                IsActive: variant.IsActive,
                SellerId: msg.SellerId,
                SellerName: sellerName,
                SellerIsActive: true,
                ImageUrl: variant.ImageUrl,
                RowVersion: variant.RowVersion);

            await sender.Send(command, context.CancellationToken);
        }

        logger.LogDebug(
            "Projected ProductInfoUpdated event for product {ProductId} with {VariantCount} active variants",
            msg.ProductId,
            msg.ActiveVariants.Count);
    }
}
