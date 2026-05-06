namespace UrbanX.Promotion.Application.Abstractions;

/// <summary>
/// Atomic Redis operations for coupon claim (Lua via Shared.Cache).
/// </summary>
public interface ICouponClaimRedisGateway
{
    /// <summary>SET NX user hold with TTL.</summary>
    Task<bool> TryAcquireUserHoldAsync(string couponCode, Guid userId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Initializes quota from DB snapshot if missing (SET NX), then DECR.
    /// On exhaustion, rolls back INCR quota and deletes user hold.
    /// </summary>
    /// <returns>true if a slot was consumed; false if exhausted (rollback applied).</returns>
    Task<bool> TryConsumeQuotaSlotAsync(
        string couponCode,
        Guid userId,
        int initialRemainingWhenKeyMissing,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically removes the per-user hold and optionally increments remaining quota (INCR).
    /// </summary>
    Task ReleaseClaimRedisStateAsync(string couponCode, Guid userId, bool incrementQuotaRemaining, CancellationToken ct = default);

    /// <summary>
    /// Deletes only <c>coupon:{code}:user:{userId}</c> (DEL). For reconciliation after a partial/failed release — avoids blind INCR retries.
    /// </summary>
    Task DeleteUserHoldAsync(string couponCode, Guid userId, CancellationToken ct = default);
}
