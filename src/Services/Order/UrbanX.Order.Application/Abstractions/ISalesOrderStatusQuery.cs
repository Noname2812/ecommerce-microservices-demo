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
    /// <summary>
    /// Sales flow now uses Redis Lua coupon lock (TASK-08) — no claim-id surfaces. Kept on the
    /// projection so the Normal-flow status query can reuse the shape; always <c>null</c> for Sales.
    /// </summary>
    Guid? CouponClaimId,
    /// <summary>True while the user's coupon is held in <c>coupon:{code}:locked-users</c>.</summary>
    bool CouponLocked,
    string? FailureStep,
    string? FailureReason,
    DateTimeOffset? SagaUpdatedAt);
