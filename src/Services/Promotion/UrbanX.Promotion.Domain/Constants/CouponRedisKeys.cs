namespace UrbanX.Promotion.Domain.Constants;

/// <summary>
/// Redis key conventions for coupon operations (raw keys; EvalAsync does not apply Shared.Cache instance prefix).
/// coupon:{code}:quota              — remaining quota (DECR on claim)
/// coupon:{code}:user:{userId}      — per-user hold (SET NX, TTL 15 min)
/// coupon:claim:idem:{key}          — cached ClaimCouponResult by OrderIdempotencyKey (TTL 24h)
/// </summary>
public static class CouponRedisKeys
{
    public static string Quota(string code) => $"coupon:{code}:quota";

    public static string UserLock(string code, Guid userId) => $"coupon:{code}:user:{userId}";

    public static string IdempotencyResult(string idempotencyKey) => $"coupon:claim:idem:{idempotencyKey}";

    /// <summary>Cart-time hold token → <see cref="UrbanX.Promotion.Application.Abstractions.CouponHoldInfo"/>.</summary>
    public static string Hold(string token) => $"coupon:hold:{token}";
}
