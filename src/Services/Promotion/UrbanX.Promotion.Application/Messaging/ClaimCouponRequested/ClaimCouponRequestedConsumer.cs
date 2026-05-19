using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Messaging;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Application.Messaging.ClaimCouponRequested;

public sealed class ClaimCouponRequestedConsumer(
    ISender mediator,
    IPublishEndpoint publishEndpoint,
    ILogger<ClaimCouponRequestedConsumer> logger)
    : IntegrationEventConsumerBase<ClaimCouponRequestedV1, ClaimCouponRequestedConsumer>(logger)
{
    protected override async Task HandleAsync(ClaimCouponRequestedV1 @event, CancellationToken cancellationToken)
    {
        var command = new ClaimCouponCommand(
            IdempotencyKey: @event.OrderIdempotencyKey,
            CouponCode: @event.CouponCode,
            UserId: Guid.Parse(@event.UserId),
            OrderAmount: @event.OrderTotal
        );

        var result = await mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            await publishEndpoint.Publish(new CouponClaimedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                ClaimId = result.Value.ClaimId,
                DiscountAmount = result.Value.DiscountAmount,
                ExpiresAt = result.Value.ExpiresAt
            }, cancellationToken);
        }
        else
        {
            await publishEndpoint.Publish(new CouponClaimFailedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                ErrorCode = result.Error!.Code,
                ErrorMessage = result.Error!.Message
            }, cancellationToken);
        }
    }
}
