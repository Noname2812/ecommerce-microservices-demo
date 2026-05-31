using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Infrastructure.Messaging.ClaimCouponRequested;

public sealed class ClaimCouponRequestedConsumer : IConsumer<ClaimCouponRequestedV1>
{
    private readonly ISender _sender;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ClaimCouponRequestedConsumer> _logger;

    public ClaimCouponRequestedConsumer(
        ISender sender,
        IPublishEndpoint publishEndpoint,
        ILogger<ClaimCouponRequestedConsumer> logger)
    {
        _sender = sender;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClaimCouponRequestedV1> context)
    {
        var @event = context.Message;
        var command = new ClaimCouponCommand(
            IdempotencyKey: @event.OrderIdempotencyKey,
            CouponCode: @event.CouponCode,
            UserId: Guid.Parse(@event.UserId),
            OrderAmount: @event.OrderTotal,
            HoldToken: @event.HoldToken);

        var result = await _sender.Send(command, context.CancellationToken);

        if (result.IsSuccess)
        {
            await _publishEndpoint.Publish(new CouponClaimedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                ClaimId = result.Value.ClaimId,
                DiscountAmount = result.Value.DiscountAmount,
                ExpiresAt = result.Value.ExpiresAt
            }, context.CancellationToken);
        }
        else
        {
            await _publishEndpoint.Publish(new CouponClaimFailedV1
            {
                OrderId = @event.OrderId,
                CorrelationId = @event.OrderId.ToString("D"),
                CausationId = @event.EventId.ToString(),
                ErrorCode = result.Error!.Code,
                ErrorMessage = result.Error!.Message
            }, context.CancellationToken);
        }
    }
}
