using Shared.Outbox.Abstractions;

namespace UrbanX.Order.Infrastructure.Services;

public sealed record ClaimCouponRequest(
    string OrderIdempotencyKey,
    string CouponCode,
    Guid UserId,
    decimal OrderAmount);

public sealed record ClaimCouponResponse(Guid ClaimId, decimal DiscountAmount, DateTimeOffset ExpiresAt);

/// <summary>
/// Context from place-order orchestration: after inventory reserve, any coupon claim failure must enqueue inventory release.
/// </summary>
public sealed record CouponClaimReservationContext(
    Guid ReservationId,
    ICompensationOutboxWriter CompensationOutboxWriter);

public interface ICouponClient
{
    /// <summary>
    /// Claims a coupon via internal Promotion API (<c>{orderIdempotencyKey}:cpn</c>). On any fault, writes
    /// <see cref="Shared.Contract.Messaging.PlaceOrder.InventoryReleaseRequestedV1"/> with reason COUPON_CLAIM_FAILED before throwing.
    /// </summary>
    Task<ClaimCouponResponse> ClaimAsync(
        ClaimCouponRequest request,
        CouponClaimReservationContext reservationContext,
        CancellationToken cancellationToken = default);
}
