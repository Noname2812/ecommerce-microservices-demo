using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductVariantUpdatedProjectionConsumer(
    ISender sender,
    ILogger<ProductVariantUpdatedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductVariantUpdatedV1, ProductVariantUpdatedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductVariantUpdatedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
