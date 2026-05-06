namespace UrbanX.Promotion.Infrastructure.Redis;

/// <summary>
/// Atomic Redis operations for coupon claim (Lua via Shared.Cache ICacheService.EvalAsync).
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
}
