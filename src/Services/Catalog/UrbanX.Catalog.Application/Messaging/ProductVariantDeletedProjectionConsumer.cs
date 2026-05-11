using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductVariantDeletedProjectionConsumer(
    ISender sender,
    ILogger<ProductVariantDeletedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductVariantDeletedV1, ProductVariantDeletedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductVariantDeletedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
