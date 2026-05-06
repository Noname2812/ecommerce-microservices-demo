namespace Shared.Contract.Messaging.PlaceOrder;

public interface ICouponClaimed : IPlaceOrderIntegrationContract
{
    Guid ClaimId { get; }
    string CouponCode { get; }
    Guid UserId { get; }
    decimal DiscountAmount { get; }
    DateTimeOffset ExpiresAt { get; }
}

public interface ICouponClaimFailed : IPlaceOrderIntegrationContract
{
    string OrderIdempotencyKey { get; }
    string CouponCode { get; }
    string Reason { get; }
}

public interface ICouponReleaseRequested : IPlaceOrderIntegrationContract
{
    Guid ClaimId { get; }
    string Reason { get; }
    string? CorrelationId { get; }
}
