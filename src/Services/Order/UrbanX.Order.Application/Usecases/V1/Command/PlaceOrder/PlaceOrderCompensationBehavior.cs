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
            return await next(cancellationToken);
        }
        catch (Exception ex)
        {
            await TryWriteCompensationAsync(ex);
            throw;
        }
    }

    private static string GetReason(Exception ex) => ex switch
    {
        OperationCanceledException => "ORDER_CANCELLED",
        _ => "ORDER_SAVE_FAILED"
    };

    private async Task TryWriteCompensationAsync(Exception originalException)
    {
        if (compensationContext.ReservationId is not Guid reservationId)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICompensationOutboxWriter>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var reason = GetReason(originalException);

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
                "Failed to write compensation after order save failure. ReservationId={ReservationId} ClaimId={ClaimId} OriginalError={OriginalError}",
                compensationContext.ReservationId,
                compensationContext.CouponClaimId,
                originalException.Message);
        }
    }
}
