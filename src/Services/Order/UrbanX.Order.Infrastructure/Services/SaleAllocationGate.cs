using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Infrastructure.Services;

internal sealed class SaleAllocationGate(IOptions<SaleAllocationOptions> options, ICacheService cache)
    : ISaleAllocationGate
{
    private const string TryReserveLua = @"
local globalQty = tonumber(redis.call('GET', KEYS[1])) or 0
local userQty   = tonumber(redis.call('GET', KEYS[2])) or 0
local qty       = tonumber(ARGV[1])
local perUser   = tonumber(ARGV[2])
if globalQty < qty then return -1 end
if userQty + qty > perUser then return -2 end
redis.call('DECRBY', KEYS[1], qty)
redis.call('INCRBY', KEYS[2], qty)
redis.call('EXPIRE', KEYS[2], 86400)
return 0";

    private const string ReleaseLua = @"
redis.call('INCRBY', KEYS[1], ARGV[1])
redis.call('DECRBY', KEYS[2], ARGV[1])
return 0";

    public async Task<Result<string>> TryReserveAsync(
        Guid campaignId, Guid userId, int totalQty, CancellationToken ct)
    {
        var globalKey = $"sale:{campaignId}:quota";
        var userKey   = $"sale:{campaignId}:user:{userId}";

        var perUserMax = Math.Max(1, options.Value.DefaultPerUserMax);

        var redisResult = await cache.EvalAsync(
            TryReserveLua,
            keys: [(RedisKey)globalKey, (RedisKey)userKey],
            args: [(RedisValue)totalQty.ToString(), (RedisValue)perUserMax.ToString()],
            ct);

        var code = (long)redisResult;

        return code switch
        {
            0  => Result.Success(globalKey),
            -1 => Result.Failure<string>(OrderSaleAllocationErrors.SaleQuotaExceeded),
            -2 => Result.Failure<string>(OrderSaleAllocationErrors.SaleUserLimitExceeded),
            _  => Result.Failure<string>(OrderSaleAllocationErrors.SaleQuotaExceeded)
        };
    }

    public async Task ReleaseAsync(
        string quotaKey, Guid campaignId, Guid userId, int totalQty, CancellationToken ct)
    {
        var userKey = $"sale:{campaignId}:user:{userId}";
        await cache.EvalAsync(
            ReleaseLua,
            keys: [(RedisKey)quotaKey, (RedisKey)userKey],
            args: [(RedisValue)totalQty.ToString()],
            ct);
    }
}
