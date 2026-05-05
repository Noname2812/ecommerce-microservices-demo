using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Messaging;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// <see cref="IMediator"/> is only passed to <see cref="IntegrationEventConsumerBase{TEvent,TConsumer}"/> (base default would publish notifications);
/// this consumer overrides <see cref="HandleAsync"/> entirely and delegates work to <see cref="InventoryReleaseRequestedProcessor"/>.
/// </summary>
public sealed class InventoryReleaseRequestedConsumer(
    IMediator mediator,
    ILogger<InventoryReleaseRequestedConsumer> logger,
    InventoryReleaseRequestedProcessor processor)
    : IntegrationEventConsumerBase<InventoryReleaseRequestedV1, InventoryReleaseRequestedConsumer>(mediator, logger)
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
