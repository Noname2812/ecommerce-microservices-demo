using UrbanX.Order.Application.Usecases.V1.Query.GetSalesOrderStatus;

namespace UrbanX.Order.Application.Abstractions;

public interface ISalesOrderStatusQuery
{
    Task<SalesOrderStatusProjection?> GetAsync(Guid orderId, CancellationToken ct);
}

public record SalesOrderStatusProjection(
    Guid OrderId,
    Guid UserId,
    string OrderStatus,
    DateTimeOffset OrderUpdatedAt,
    string? SagaCurrentState,
    Guid? ReservationId,
    Guid? CouponClaimId,
    string? FailureStep,
    string? FailureReason,
    DateTimeOffset? SagaUpdatedAt);
