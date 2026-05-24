using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantAdded;

/// <summary>
/// The <c>ProductVariantAddedV1</c> payload does not carry <c>ProductName</c> / <c>SellerName</c>
/// because those live at the product level. We inherit them from any sibling variant of the same
/// product in the local read model; if the product is unknown locally the event is skipped — it
/// means <c>ProductCreatedV1</c> has not arrived yet, and that consumer will populate the row.
/// </summary>
public sealed class ProductVariantAddedReadModelConsumer(
    ISender sender,
    IProductVariantReadModelRepository repository,
    ILogger<ProductVariantAddedReadModelConsumer> logger)
    : IConsumer<ProductUpdateIntegrationEvents.ProductVariantAddedV1>
{
    public async Task Consume(ConsumeContext<ProductUpdateIntegrationEvents.ProductVariantAddedV1> context)
    {
        var msg = context.Message;
        var sibling = await repository.GetAnyByProductIdAsync(msg.ProductId, context.CancellationToken);

        if (sibling is null)
        {
            logger.LogWarning(
                "ProductVariantAdded received for unknown product {ProductId}; skipping (waiting for ProductCreated)",
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
            "Projected ProductVariantAdded {VariantId} for product {ProductId}",
            variant.VariantId, msg.ProductId);
    }
}
