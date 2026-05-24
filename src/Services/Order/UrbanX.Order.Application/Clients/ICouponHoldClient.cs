namespace UrbanX.Order.Application.Clients;

/// <summary>
/// Cross-service read of Promotion's <c>coupon:hold:{token}</c> Redis state. Lives on the Order side so
/// the place-order saga can verify a Cart-issued hold token with a single Redis GET — no Promotion call
/// on the order critical path.
/// </summary>
/// <remarks>
/// Mirrors <c>UrbanX.Promotion.Application.Abstractions.ICouponHoldGateway</c> from the other side. Keep
/// the key format <c>coupon:hold:{token}</c> + <c>CouponHoldSnapshot</c> shape in sync; they are an informal
/// contract carried over Redis, not over a typed wire.
/// </remarks>
public interface ICouponHoldClient
{
    /// <summary>Returns the hold metadata, or <c>null</c> when the token expired/never existed.</summary>
    Task<CouponHoldSnapshot?> TryGetAsync(string token, CancellationToken ct = default);

    /// <summary>Best-effort release. Deletes the token + frees user-lock + restores the quota slot.</summary>
    Task ReleaseAsync(string token, CancellationToken ct = default);
}

public sealed record CouponHoldSnapshot(
    string CouponCode,
    Guid UserId,
    decimal DiscountAmount,
    string DiscountType,
    decimal OrderAmount,
    DateTimeOffset ExpiresAt);
