using Microsoft.Extensions.Logging;
using Shared.Cache.Abstractions;
using StackExchange.Redis;
using UrbanX.Order.Application.Clients;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// Reads Promotion's <c>coupon:hold:{token}</c> Redis state directly. Mirrors
/// <c>UrbanX.Promotion.Infrastructure.Redis.CouponHoldGateway</c> on the consumer side — the two are an
/// informal contract carried over Redis. Key format + JSON shape must stay in sync.
/// </summary>
internal sealed class CouponHoldClient(
    ICacheService cache,
    ILogger<CouponHoldClient> logger)
    : ICouponHoldClient
{
    // Keep in sync with UrbanX.Promotion.Domain.Constants.CouponRedisKeys.
    private static string HoldKey(string token) => $"coupon:hold:{token}";
    private static string UserLockKey(string couponCode, Guid userId) => $"coupon:{couponCode}:user:{userId}";
    private static string QuotaKey(string couponCode) => $"coupon:{couponCode}:quota";

    /// <summary>
    /// One atomic Lua: DEL user-hold lock, then optional INCR quota — same script used in Promotion
    /// (<c>CouponClaimRedisGateway.ReleaseClaimScript</c>). Quota is always restored when a hold is
    /// released since the hold itself only existed because a quota slot was consumed.
    /// </summary>
    private const string ReleaseLockAndRestoreQuotaScript = """
        redis.call('DEL', KEYS[1])
        return redis.call('INCR', KEYS[2])
        """;

    public Task<CouponHoldSnapshot?> TryGetAsync(string token, CancellationToken ct = default) =>
        cache.GetAsync<CouponHoldSnapshot>(HoldKey(token), ct);

    public async Task ReleaseAsync(string token, CancellationToken ct = default)
    {
        var snapshot = await cache.GetAsync<CouponHoldSnapshot>(HoldKey(token), ct);
        if (snapshot is null)
        {
            logger.LogDebug("Coupon hold {Token} already gone — release is a no-op.", token);
            return;
        }

        try
        {
            await cache.EvalAsync(
                ReleaseLockAndRestoreQuotaScript,
                new RedisKey[] { UserLockKey(snapshot.CouponCode, snapshot.UserId), QuotaKey(snapshot.CouponCode) },
                args: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Best-effort: token deletion still runs, TTL on user-lock will mop up. Quota may drift by 1.
            logger.LogWarning(ex,
                "Failed to release user-lock/quota for coupon {CouponCode} user {UserId} during hold release. " +
                "Token will still be deleted; TTL will reconcile.",
                snapshot.CouponCode, snapshot.UserId);
        }

        await cache.RemoveAsync(HoldKey(token), ct);
    }
}
