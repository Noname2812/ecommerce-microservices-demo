namespace UrbanX.Promotion.Application.Abstractions;

/// <summary>
/// Cart-time coupon hold: Redis-only reservation that lets a user apply a coupon before checkout.
/// Decouples coupon validation from the Order saga critical path.
/// </summary>
/// <remarks>
/// Pattern: Cart calls <see cref="TryAcquireAsync"/> to reserve → receives a <c>HoldToken</c>
/// (TTL 15 min). At checkout the Order saga verifies the token with <see cref="TryGetAsync"/> (single Redis GET).
/// If the user removes the coupon at Cart, call <see cref="TryReleaseAsync"/> to free the slot before TTL.
/// Persistent DB claim only happens AFTER payment succeeds (off the order critical path).
/// </remarks>
public interface ICouponHoldGateway
{
    /// <summary>
    /// Persist hold metadata under a fresh token; safe to call after a user-lock + quota-slot have been acquired.
    /// </summary>
    Task SetHoldAsync(string token, CouponHoldInfo info, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Retrieve hold metadata by token. Returns <c>null</c> when expired/missing.</summary>
    Task<CouponHoldInfo?> TryGetAsync(string token, CancellationToken ct = default);

    /// <summary>Atomically remove the hold token. Returns <c>true</c> when a token was deleted.</summary>
    Task<bool> TryDeleteAsync(string token, CancellationToken ct = default);
}

public sealed record CouponHoldInfo(
    string CouponCode,
    Guid UserId,
    decimal DiscountAmount,
    string DiscountType,
    decimal OrderAmount,
    DateTimeOffset ExpiresAt);
