using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Infrastructure.Services;

public sealed class RedisFlashSaleStockService(
    ICacheService cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisFlashSaleStockService> logger)
    : IFlashSaleStockService
{
    private readonly string _prefix = $"{cacheOptions.Value.InstanceName}:flashsale";

    private const string ReserveScript = """
        local stock = tonumber(redis.call('GET', KEYS[1]) or '0')
        if stock < tonumber(ARGV[1]) then return -1 end
        return redis.call('DECRBY', KEYS[1], ARGV[1])
        """;

    public async Task<Result> TryReserveAsync(Guid saleId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0)
            return Result.Success();

        var key = StockKey(saleId);
        var redisResult = await cache.EvalAsync(ReserveScript, [key], [quantity], ct);
        var remaining = ToInt64(redisResult);

        if (remaining < 0)
        {
            logger.LogWarning(
                "Flash sale stock insufficient for campaign {CampaignId}, requested {Quantity}",
                saleId,
                quantity);
            return Result.Failure(OrderErrors.FlashSaleSoldOut(saleId));
        }

        return Result.Success();
    }

    /// <summary>
    /// Best-effort restore. Failures are swallowed-and-logged so a transient Redis blip during
    /// saga compensation can't abort the rest of the compensation chain (cancel order, release
    /// pending slot, etc.). Over-counting stock on a missed restore is acceptable: campaign
    /// reconciliation will rebalance on next refresh; under-counting (the alternative if we
    /// throw) would block downstream cleanup and leave the user holding a pending slot.
    /// </summary>
    public async Task RestoreAsync(Guid saleId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0) return;

        try
        {
            await cache.EvalAsync(
                "return redis.call('INCRBY', KEYS[1], ARGV[1])",
                [StockKey(saleId)],
                [quantity],
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Flash sale stock restore failed for {CampaignId} (+{Quantity}); reconciliation must repair",
                saleId, quantity);
        }
    }

    private string StockKey(Guid saleId) => $"{_prefix}:{saleId:D}:stock";

    private static long ToInt64(RedisResult result) =>
        result.IsNull ? 0 : (long)result;
}
