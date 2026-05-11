using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Messaging;
using UrbanX.Catalog.Application.Usecases.V1.Command.RefreshProductProjection;
using static Shared.Contract.Messaging.Catalog.ProductUpdateIntegrationEvents;

namespace UrbanX.Catalog.Application.Messaging;

public sealed class ProductStatusChangedProjectionConsumer(
    ISender sender,
    ILogger<ProductStatusChangedProjectionConsumer> logger)
    : IntegrationEventConsumerBase<ProductStatusChangedV1, ProductStatusChangedProjectionConsumer>(logger)
{
    protected override Task HandleAsync(ProductStatusChangedV1 @event, CancellationToken ct)
        => sender.Send(new RefreshProductProjectionCommand(@event.ProductId), ct);
}
