using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using UrbanX.Order.Application.Usecases.V1.Command.UpdateProductStatusProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductStatusChanged;

public sealed class ProductStatusChangedReadModelConsumer(
    ISender sender,
    ILogger<ProductStatusChangedReadModelConsumer> logger)
    : IConsumer<ProductUpdateIntegrationEvents.ProductStatusChangedV1>
{
    public async Task Consume(ConsumeContext<ProductUpdateIntegrationEvents.ProductStatusChangedV1> context)
    {
        var msg = context.Message;
        var isActive = string.Equals(msg.NewStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase);

        await sender.Send(
            new UpdateProductStatusProjectionCommand(msg.ProductId, isActive),
            context.CancellationToken);

        logger.LogDebug(
            "Projected ProductStatusChanged for {ProductId}: {OldStatus} -> {NewStatus}",
            msg.ProductId, msg.OldStatus, msg.NewStatus);
    }
}
