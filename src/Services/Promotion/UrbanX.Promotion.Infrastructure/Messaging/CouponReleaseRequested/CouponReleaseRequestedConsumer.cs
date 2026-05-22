using MassTransit;
using MediatR;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Infrastructure.Messaging.CouponReleaseRequested;

public sealed class CouponReleaseRequestedConsumer(ISender sender) : IConsumer<CouponReleaseRequestedV1>
{
    public Task Consume(ConsumeContext<CouponReleaseRequestedV1> context)
    {
        var command = new ReleaseCouponClaimCommand(
            ClaimId: context.Message.ClaimId,
            EventId: context.Message.EventId);

        return sender.Send(command, context.CancellationToken);
    }
}
