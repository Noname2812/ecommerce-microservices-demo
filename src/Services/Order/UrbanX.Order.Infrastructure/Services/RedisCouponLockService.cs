using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// Redis Lua-based coupon lock for the Sales flow (TASK-08). Avoids the event-based ClaimCoupon
/// round-trip — saga locks atomically, payment outcome decides confirm vs release.
///
/// Lock-time options are read via <see cref="IOptionsMonitor{TOptions}"/> so config reloads take
/// effect without restarting the host (this service is registered as a singleton).
/// </summary>
internal sealed class RedisCouponLockService(
    ICacheService cache,
    IOptions<CacheOptions> cacheOptions,
    IOptionsMonitor<PlaceOrderOptions> placeOrderOptions,
    ILogger<RedisCouponLockService> logger)
    : ICouponLockService
{
    private readonly string _prefix = $"{cacheOptions.Value.InstanceName}:coupon";

    /// <summary>
    /// Atomic Lua: SISMEMBER used → SISMEMBER eligible → SISMEMBER locked → GET remaining →
    /// DECR remaining + SADD locked + EXPIRE → return "amount|type" bulk string.
    /// Error codes (integer): -1 used, -2 not-eligible, -3 already-locked, -4 exhausted.
    /// </summary>
    private const string ReserveScript = """
        if redis.call('SISMEMBER', KEYS[4], ARGV[1]) == 1 then return -1 end
        if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 0 then return -2 end
        if redis.call('SISMEMBER', KEYS[3], ARGV[1]) == 1 then return -3 end

        local remaining = tonumber(redis.call('GET', KEYS[2]) or '0')
        if remaining <= 0 then return -4 end

        redis.call('DECR', KEYS[2])
        redis.call('SADD', KEYS[3], ARGV[1])
        redis.call('EXPIRE', KEYS[3], ARGV[2])

        local amount = redis.call('GET', KEYS[5]) or '0'
        local dtype  = redis.call('GET', KEYS[6]) or 'FIXED'
        return amount .. '|' .. dtype
        """;

    private const string ReleaseScript = """
        if redis.call('SREM', KEYS[3], ARGV[1]) == 1 then
            redis.call('INCR', KEYS[2])
        end
        return 1
        """;

    private const string ConfirmUseScript = """
        redis.call('SREM', KEYS[3], ARGV[1])
        redis.call('SADD', KEYS[4], ARGV[1])
        return 1
        """;

    public async Task<Result<CouponLockInfo>> TryLockAsync(string couponCode, Guid userId, CancellationToken ct)
    {
        var keys = BuildKeys(couponCode);
        RedisValue[] args = [userId.ToString("D"), placeOrderOptions.CurrentValue.CouponLockTtlSeconds];

        var raw = await cache.EvalAsync(ReserveScript, keys, args, ct);

        if (raw.Resp2Type == ResultType.Integer)
        {
            var code = (long)raw;
            logger.LogWarning(
                "Coupon lock failed for {Code}/{UserId}: result {ResultCode}",
                couponCode,
                userId,
                code);

            return code switch
            {
                -1 => Result.Failure<CouponLockInfo>(OrderErrors.CouponAlreadyUsed),
                -2 => Result.Failure<CouponLockInfo>(OrderErrors.CouponNotEligible),
                // -3 = user is in locked-users (concurrent in-flight saga), distinct from
                // -1 (permanently used). TTL on locked-users (16 min by default) will clear.
                -3 => Result.Failure<CouponLockInfo>(OrderErrors.CouponConcurrentClaim),
                -4 => Result.Failure<CouponLockInfo>(OrderErrors.CouponExhausted),
                _ => Result.Failure<CouponLockInfo>(OrderErrors.CouponClaimFailed("Coupon lock failed"))
            };
        }

        var payload = (string?)raw ?? string.Empty;
        var pipe = payload.IndexOf('|');
        if (pipe < 0)
            return Result.Failure<CouponLockInfo>(OrderErrors.CouponClaimFailed("Coupon metadata malformed"));

        var amountStr = payload[..pipe];
        var dtype = payload[(pipe + 1)..];

        if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return Result.Failure<CouponLockInfo>(OrderErrors.CouponClaimFailed("Coupon discount amount malformed"));

        var resolvedType = CouponDiscountTypes.IsValid(dtype) ? dtype : CouponDiscountTypes.Fixed;
        return Result.Success(new CouponLockInfo(amount, resolvedType));
    }

    /// <summary>
    /// Best-effort release. Failures are swallowed-and-logged: the 16-min TTL on the locked-users
    /// set is the safety net, and a transient Redis blip must not crash the saga compensation chain.
    /// </summary>
    public async Task ReleaseAsync(string couponCode, Guid userId, CancellationToken ct)
    {
        try
        {
            await cache.EvalAsync(ReleaseScript, BuildKeys(couponCode), [userId.ToString("D")], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Coupon release failed for {Code}/{UserId}; locked-users TTL will reclaim",
                couponCode, userId);
        }
    }

    /// <summary>
    /// Best-effort confirm. If this fails the order is still marked paid; the locked-users TTL
    /// will eventually drop the user, and the used-users set entry can be reconciled by the
    /// Promotion service's daily job from the persisted Order.CouponCode.
    /// </summary>
    public async Task ConfirmUseAsync(string couponCode, Guid userId, CancellationToken ct)
    {
        try
        {
            await cache.EvalAsync(ConfirmUseScript, BuildKeys(couponCode), [userId.ToString("D")], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Coupon confirm-use failed for {Code}/{UserId}; reconciliation job must repair",
                couponCode, userId);
        }
    }

    /// <remarks>
    /// Key order matches Lua KEYS[]:
    ///   [1] eligible-users · [2] remaining · [3] locked-users · [4] used-users
    ///   [5] meta-discount  · [6] meta-discount-type
    /// </remarks>
    private RedisKey[] BuildKeys(string code) =>
    [
        $"{_prefix}:{code}:eligible-users",
        $"{_prefix}:{code}:remaining",
        $"{_prefix}:{code}:locked-users",
        $"{_prefix}:{code}:used-users",
        $"{_prefix}:{code}:meta-discount",
        $"{_prefix}:{code}:meta-discount-type"
    ];
}
