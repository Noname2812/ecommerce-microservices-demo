using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Messaging;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Uses the logger-only <see cref="IntegrationEventConsumerBase{TEvent,TConsumer}"/> constructor; <see cref="HandleAsync"/> delegates to
/// <see cref="InventoryReleaseRequestedProcessor"/> (scoped <c>IMediator</c> there dispatches the release command).
/// </summary>
public sealed class InventoryReleaseRequestedConsumer(
    ILogger<InventoryReleaseRequestedConsumer> logger,
    InventoryReleaseRequestedProcessor processor)
    : IntegrationEventConsumerBase<InventoryReleaseRequestedV1, InventoryReleaseRequestedConsumer>(logger)
{
    private readonly InventoryReleaseRequestedProcessor _processor = processor;

    /// <summary>
    /// Maps release failures to "transient" logging so endpoint-level <c>UseMessageRetry</c> retries do not emit fatal-level noise per attempt.
    /// </summary>
    protected override bool IsTransient(Exception ex) =>
        ex is InventoryReleaseCommandFailedException || base.IsTransient(ex);

    protected override Task HandleAsync(InventoryReleaseRequestedV1 @event, CancellationToken cancellationToken)
        => _processor.ProcessAsync(@event, cancellationToken);
}
