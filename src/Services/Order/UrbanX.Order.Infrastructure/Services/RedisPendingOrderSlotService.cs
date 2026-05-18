using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Infrastructure.Services;

public sealed class RedisPendingOrderSlotService(
    ICacheService cache,
    IOptions<PlaceOrderOptions> options,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisPendingOrderSlotService> logger)
    : IPendingOrderSlotService
{
    private readonly PlaceOrderOptions _opts = options.Value;
    private readonly string _prefix = $"{cacheOptions.Value.InstanceName}:pending-orders";

    private const string AcquireScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        if current > tonumber(ARGV[2]) then
            redis.call('DECR', KEYS[1])
            return 0
        end
        return current
        """;

    private const string ReleaseScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current <= 0 then return 0 end
        return redis.call('DECR', KEYS[1])
        """;

    public async Task<Result> TryAcquireAsync(Guid userId, string orderType, CancellationToken ct)
    {
        var (key, max) = orderType == OrderType.Sales
            ? ($"{_prefix}:sales:{userId:D}", _opts.MaxSalesPendingPerUser)
            : ($"{_prefix}:normal:{userId:D}", _opts.MaxNormalPendingPerUser);

        var redisResult = await cache.EvalAsync(
            AcquireScript,
            [key],
            [_opts.PendingSlotTtlMinutes * 60, max],
            ct);

        var slot = ToInt64(redisResult);
        if (slot == 0)
        {
            logger.LogWarning(
                "Pending order slot rejected for user {UserId}, orderType {OrderType}, max {Max}",
                userId,
                orderType,
                max);
            return Result.Failure(OrderErrors.TooManyPendingOrders);
        }

        return Result.Success();
    }

    public Task ReleaseAsync(Guid userId, CancellationToken ct) =>
        Task.WhenAll(
            cache.EvalAsync(ReleaseScript, [$"{_prefix}:normal:{userId:D}"], null, ct),
            cache.EvalAsync(ReleaseScript, [$"{_prefix}:sales:{userId:D}"], null, ct));

    private static long ToInt64(RedisResult result) =>
        result.IsNull ? 0 : (long)result;
}
