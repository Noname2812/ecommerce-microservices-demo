namespace UrbanX.Promotion.Domain.Constants;

/// <summary>
/// Redis key conventions for coupon operations (raw keys; EvalAsync does not apply Shared.Cache instance prefix).
/// coupon:{code}:quota — remaining quota (DECR on claim)
/// coupon:{code}:user:{userId} — per-user hold (SET NX, TTL 15 min)
/// </summary>
public static class CouponRedisKeys
{
    public static string Quota(string code) => $"coupon:{code}:quota";

    public static string UserLock(string code, Guid userId) => $"coupon:{code}:user:{userId}";
}
