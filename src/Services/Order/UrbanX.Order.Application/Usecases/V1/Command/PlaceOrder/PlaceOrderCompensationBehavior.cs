using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCompensationBehavior(
    PlaceOrderCompensationContext compensationContext,
    IServiceScopeFactory scopeFactory,
    ILogger<PlaceOrderCompensationBehavior> logger)
    : IPipelineBehavior<PlaceOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        PlaceOrderCommand request,
        RequestHandlerDelegate<Result<Guid>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await next(cancellationToken);
            if (result.IsFailure && compensationContext.ReservationId is Guid)
                await TryWriteCompensationAsync(result.Error.Code);
            return result;
        }
        catch (Exception ex)
        {
            await TryWriteCompensationAsync(ex switch
            {
                OperationCanceledException => "ORDER_CANCELLED",
                _ => "ORDER_SAVE_FAILED"
            });
            throw;
        }
    }

    private async Task TryWriteCompensationAsync(string reason)
    {
        if (compensationContext.ReservationId is not Guid reservationId)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICompensationOutboxWriter>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.ExecuteInTransactionAsync(async () =>
            {
                await writer.AddAsync(new InventoryReleaseRequestedV1
                {
                    ReservationId = reservationId,
                    Reason = reason
                }, CancellationToken.None);

                if (compensationContext.CouponClaimId is Guid claimId)
                {
                    await writer.AddAsync(new CouponReleaseRequestedV1
                    {
                        ClaimId = claimId,
                        Reason = reason
                    }, CancellationToken.None);
                }
            }, CancellationToken.None);
        }
        catch (Exception compensationEx)
        {
            logger.LogError(
                compensationEx,
                "Failed to write compensation after order failure. ReservationId={ReservationId} ClaimId={ClaimId} Reason={Reason}",
                compensationContext.ReservationId,
                compensationContext.CouponClaimId,
                reason);
        }
    }
}
