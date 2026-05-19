using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Messaging;

namespace UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

/// <summary>
/// Logger-only base ctor: work is delegated to <see cref="CouponReleaseRequestedProcessor"/>, which gets its own scoped <c>IMediator</c> from DI.
/// </summary>
public sealed class CouponReleaseRequestedConsumer(
    ILogger<CouponReleaseRequestedConsumer> logger,
    CouponReleaseRequestedProcessor processor)
    : IntegrationEventConsumerBase<CouponReleaseRequestedV1, CouponReleaseRequestedConsumer>(logger)
{
    private readonly CouponReleaseRequestedProcessor _processor = processor;

    protected override bool IsTransient(Exception ex) =>
        CouponReleaseRequestedTransientClassification.IsTransient(ex, base.IsTransient);

    protected override Task HandleAsync(CouponReleaseRequestedV1 @event, CancellationToken cancellationToken)
        => _processor.ProcessAsync(@event, cancellationToken);
}
