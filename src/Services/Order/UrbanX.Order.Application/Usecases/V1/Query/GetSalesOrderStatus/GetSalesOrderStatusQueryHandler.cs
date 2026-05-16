using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Application.Usecases.V1.Query.GetSalesOrderStatus;

public sealed class GetSalesOrderStatusQueryHandler(
    ISalesOrderStatusQuery statusQuery,
    IUserContext userContext)
    : IQueryHandler<GetSalesOrderStatusQuery, SalesOrderStatusDto>
{
    public async Task<Result<SalesOrderStatusDto>> Handle(
        GetSalesOrderStatusQuery query, CancellationToken ct)
    {
        var data = await statusQuery.GetAsync(query.OrderId, ct);

        if (data is null)
            return Result.Failure<SalesOrderStatusDto>(OrderErrors.NotFound(query.OrderId));

        if (data.UserId != userContext.UserId)
            return Result.Failure<SalesOrderStatusDto>(OrderErrors.Forbidden);

        return Result.Success(new SalesOrderStatusDto(
            OrderId:       data.OrderId,
            OrderStatus:   data.OrderStatus,
            SagaState:     data.SagaCurrentState ?? "Pending",
            ReservationId: data.ReservationId,
            CouponClaimId: data.CouponClaimId,
            FailureStep:   data.FailureStep,
            FailureReason: data.FailureReason,
            UpdatedAt:     data.SagaUpdatedAt ?? data.OrderUpdatedAt));
    }
}
