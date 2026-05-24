using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.MarkProductVariantDeleted;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantDeleted;

public sealed class ProductVariantDeletedReadModelConsumer(
    ISender sender,
    ILogger<ProductVariantDeletedReadModelConsumer> logger)
    : IConsumer<ProductUpdateIntegrationEvents.ProductVariantDeletedV1>
{
    public async Task Consume(ConsumeContext<ProductUpdateIntegrationEvents.ProductVariantDeletedV1> context)
    {
        var msg = context.Message;
        await sender.Send(new MarkProductVariantDeletedCommand(msg.VariantId), context.CancellationToken);

        logger.LogDebug(
            "Projected ProductVariantDeleted {VariantId} (Sku {Sku})",
            msg.VariantId, msg.Sku);
    }
}
