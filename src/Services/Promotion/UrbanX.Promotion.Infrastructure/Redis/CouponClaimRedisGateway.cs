using Shared.Cache.Abstractions;
using StackExchange.Redis;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Domain.Constants;

namespace UrbanX.Promotion.Infrastructure.Redis;

internal sealed class CouponClaimRedisGateway(ICacheService cache) : ICouponClaimRedisGateway
{
    private const string AcquireUserHoldScript = """
        local ok = redis.call('SET', KEYS[1], '1', 'NX', 'EX', ARGV[1])
        if ok then return 1 else return 0 end
        """;

    /// <summary>
    /// If quota key missing, SET initial remaining with NX (no overwrite). Then DECR; rollback if negative.
    /// </summary>
    private const string ConsumeQuotaScript = """
        local q = KEYS[1]
        local userHold = KEYS[2]
        local init = tonumber(ARGV[1])
        local cur = redis.call('GET', q)
        if cur == false then
          redis.call('SET', q, init, 'NX')
        end
        local v = redis.call('DECR', q)
        if v < 0 then
          redis.call('INCR', q)
          redis.call('DEL', userHold)
          return -1
        end
        return v
        """;

    /// <summary>Atomic: DEL user hold, then optional INCR quota (one round-trip).</summary>
    private const string ReleaseClaimScript = """
        redis.call('DEL', KEYS[1])
        if tonumber(ARGV[1]) == 1 then
          return redis.call('INCR', KEYS[2])
        end
        return 0
        """;

    public async Task<bool> TryAcquireUserHoldAsync(string couponCode, Guid userId, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = CouponRedisKeys.UserLock(couponCode, userId);
        var seconds = (int)Math.Clamp(ttl.TotalSeconds, 1, int.MaxValue);
        var r = await cache.EvalAsync(
            AcquireUserHoldScript,
            new RedisKey[] { key },
            new RedisValue[] { seconds },
            ct);
        return (long)r == 1;
    }

    public async Task<bool> TryConsumeQuotaSlotAsync(
        string couponCode,
        Guid userId,
        int initialRemainingWhenKeyMissing,
        CancellationToken ct = default)
    {
        var quotaKey = CouponRedisKeys.Quota(couponCode);
        var userHold = CouponRedisKeys.UserLock(couponCode, userId);
        var r = await cache.EvalAsync(
            ConsumeQuotaScript,
            new RedisKey[] { quotaKey, userHold },
            new RedisValue[] { initialRemainingWhenKeyMissing },
            ct);
        var code = (long)r;
        return code >= 0;
    }

    public async Task ReleaseClaimRedisStateAsync(
        string couponCode,
        Guid userId,
        bool incrementQuotaRemaining,
        CancellationToken ct = default)
    {
        var userHoldKey = CouponRedisKeys.UserLock(couponCode, userId);
        var quotaKey = CouponRedisKeys.Quota(couponCode);
        var flag = incrementQuotaRemaining ? 1 : 0;
        await cache.EvalAsync(
            ReleaseClaimScript,
            new RedisKey[] { userHoldKey, quotaKey },
            new RedisValue[] { flag },
            ct);
    }

    public Task DeleteUserHoldAsync(string couponCode, Guid userId, CancellationToken ct = default) =>
        cache.RemoveAsync(CouponRedisKeys.UserLock(couponCode, userId), ct);
}
