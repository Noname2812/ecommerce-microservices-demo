using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Catalog;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductCreatedProjectionConsumer(
    ISender sender,
    ILogger<ProductCreatedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductCreatedV1, ProductCreatedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductCreatedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
