using MediatR;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.Application.Messaging;

public sealed class CouponReleaseRequestedProcessor(IMediator mediator)
{
    public async Task ProcessAsync(CouponReleaseRequestedV1 @event, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ReleaseCouponClaimCommand(@event.ClaimId, @event.EventId),
            cancellationToken);

        if (result.IsFailure)
            throw new CouponReleaseCommandFailedException(result.Error);
    }
}
