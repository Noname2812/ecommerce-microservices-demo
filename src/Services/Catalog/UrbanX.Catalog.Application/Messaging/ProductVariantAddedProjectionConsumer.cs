using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductVariantAddedProjectionConsumer(
    ISender sender,
    ILogger<ProductVariantAddedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductVariantAddedV1, ProductVariantAddedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductVariantAddedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
