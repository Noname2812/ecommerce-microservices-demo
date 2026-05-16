using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCompensationBehavior(
    PlaceSalesOrderCompensationContext salesCompensationContext,
    IServiceScopeFactory scopeFactory,
    ILogger<PlaceSalesOrderCompensationBehavior> logger)
    : IPipelineBehavior<PlaceSalesOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        PlaceSalesOrderCommand request,
        RequestHandlerDelegate<Result<Guid>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await next(cancellationToken);
            if (result.IsFailure && salesCompensationContext.HasSaleAllocation)
                await TryWriteCompensationAsync(new SalesOrderCompensationReasonException(result.Error));
            return result;
        }
        catch (Exception ex)
        {
            await TryWriteCompensationAsync(ex);
            throw;
        }
    }

    private async Task TryWriteCompensationAsync(Exception originalException)
    {
        if (!salesCompensationContext.HasSaleAllocation)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICompensationOutboxWriter>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var reason = ResolveCompensationReason(originalException);

            await uow.ExecuteInTransactionAsync(async () =>
            {
                await writer.AddAsync(new SaleQuotaReleaseRequestedV1
                {
                    CampaignId = salesCompensationContext.SaleCampaignId!.Value,
                    UserId     = salesCompensationContext.SaleUserId!.Value,
                    Quantity   = salesCompensationContext.SaleReservedQty,
                    QuotaKey   = salesCompensationContext.SaleQuotaKey!
                }, CancellationToken.None);
            }, CancellationToken.None);
        }
        catch (Exception compensationEx)
        {
            logger.LogError(
                compensationEx,
                "Failed to write quota release compensation for SaleQuotaKey={QuotaKey} OriginalError={OriginalError}",
                salesCompensationContext.SaleQuotaKey,
                originalException.Message);
        }
    }

    private static string ResolveCompensationReason(Exception ex) => ex switch
    {
        OperationCanceledException => "ORDER_CANCELLED",
        SalesOrderCompensationReasonException r => SanitizeReason(r.ReasonCode),
        _ => "ORDER_SAVE_FAILED"
    };

    private static string SanitizeReason(string code)
    {
        var t = code.Trim();
        if (t.Length == 0) return "ORDER_SAVE_FAILED";
        return t.Length <= 256 ? t : t[..256];
    }
}
