using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductCreated;

public sealed class ProductCreatedReadModelConsumer(
    ISender sender,
    ILogger<ProductCreatedReadModelConsumer> logger)
    : IConsumer<ProductIntegrationEvents.ProductCreatedV1>
{
    public async Task Consume(ConsumeContext<ProductIntegrationEvents.ProductCreatedV1> context)
    {
        var msg = context.Message;
        var productIsActive = string.Equals(msg.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

        foreach (var variant in msg.Variants)
        {
            var command = new RefreshProductVariantProjectionCommand(
                VariantId: variant.VariantId,
                ProductId: msg.ProductId,
                ProductName: msg.Name,
                ProductIsActive: productIsActive,
                Sku: variant.Sku,
                VariantName: variant.Name,
                Price: variant.Price,
                IsActive: variant.IsActive,
                SellerId: msg.SellerId,
                SellerName: msg.SellerName,
                SellerIsActive: true,
                ImageUrl: variant.ImageUrl,
                RowVersion: variant.RowVersion);

            await sender.Send(command, context.CancellationToken);
        }

        logger.LogDebug(
            "Projected ProductCreated event for product {ProductId} with {VariantCount} variants",
            msg.ProductId,
            msg.Variants.Count);
    }
}
