using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductInfoUpdatedProjectionConsumer(
    ISender sender,
    ILogger<ProductInfoUpdatedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductInfoUpdatedV1, ProductInfoUpdatedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductInfoUpdatedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
