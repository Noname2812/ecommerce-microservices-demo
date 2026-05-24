using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record ClaimCouponRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string OrderIdempotencyKey { get; init; }
    public required string UserId { get; init; }
    public required string CouponCode { get; init; }
    public required decimal OrderTotal { get; init; }

    /// <summary>
    /// When set, the claim originated from a Cart-time hold (Phase 3 flow). Promotion's handler
    /// skips the Redis user-lock + quota-slot acquire because those were already consumed at hold time.
    /// Null = legacy direct claim path (handler does the full Redis acquire).
    /// </summary>
    public string? HoldToken { get; init; }
}

public record CouponClaimedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required Guid ClaimId { get; init; }
    public required decimal DiscountAmount { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public record CouponClaimFailedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
}
