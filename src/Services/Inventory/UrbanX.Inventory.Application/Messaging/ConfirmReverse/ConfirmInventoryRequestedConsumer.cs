using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Messaging;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ConfirmInventoryRequestedConsumer(
    ILogger<ConfirmInventoryRequestedConsumer> logger,
    ConfirmInventoryRequestedProcessor processor)
    : IntegrationEventConsumerBase<ConfirmInventoryRequestedV1, ConfirmInventoryRequestedConsumer>(logger)
{
    protected override bool IsTransient(Exception ex) =>
        ConfirmInventoryTransientClassifier.IsTransient(ex, base.IsTransient);

    protected override Task HandleAsync(ConfirmInventoryRequestedV1 @event, CancellationToken cancellationToken)
        => processor.ProcessAsync(@event, cancellationToken);
}
