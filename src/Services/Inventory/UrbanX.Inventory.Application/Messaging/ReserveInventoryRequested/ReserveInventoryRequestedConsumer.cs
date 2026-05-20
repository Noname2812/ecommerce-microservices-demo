using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Messaging;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ReserveInventoryRequestedConsumer(
    ILogger<ReserveInventoryRequestedConsumer> logger,
    ReserveInventoryRequestedProcessor processor)
    : IntegrationEventConsumerBase<ReserveInventoryRequestedV1, ReserveInventoryRequestedConsumer>(logger)
{
    protected override Task HandleAsync(ReserveInventoryRequestedV1 @event, CancellationToken ct)
        => processor.ProcessAsync(@event, ct);
}
