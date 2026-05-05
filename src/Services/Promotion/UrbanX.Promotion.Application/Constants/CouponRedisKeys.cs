namespace UrbanX.Promotion.Application.Constants;

/// <summary>
/// Redis key conventions for coupon operations.
/// coupon:{code}:quota         — global remaining quota counter (DECR on claim, INCR on rollback)
/// coupon:{code}:user:{userId} — per-user lock, TTL 15 min (SET NX; prevents double-claim)
/// </summary>
public static class CouponRedisKeys
{
    public static string Quota(string code) => $"coupon:{code}:quota";
    public static string UserLock(string code, Guid userId) => $"coupon:{code}:user:{userId}";
}
