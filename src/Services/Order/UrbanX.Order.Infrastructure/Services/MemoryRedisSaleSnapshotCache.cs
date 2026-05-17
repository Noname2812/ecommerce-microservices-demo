using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Promotion;
using UrbanX.Order.Application.Constants;
using UrbanX.Order.Application.Telemetry;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// L1 in-process <see cref="IMemoryCache"/> + L2 Redis lookup for sale campaign meta and sale prices.
/// Promotion service's warm-up cronjob is the only writer to Redis; we never call HTTP — cache miss
/// surfaces as a domain failure (campaign unavailable / pricing unavailable) so high-throughput sale
/// traffic never blocks on a sync upstream call.
/// </summary>
internal sealed class MemoryRedisSaleSnapshotCache(
    IMemoryCache memoryCache,
    ICacheService redis,
    IOptions<SaleSnapshotOptions> options)
    : ISaleSnapshotCache
{
    private readonly TimeSpan _memoryTtl = TimeSpan.FromSeconds(Math.Max(1, options.Value.MemoryCacheTtlSeconds));

    public async Task<Result<CampaignSnapshot?>> GetCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var validator = SaleProjectionConstants.ValidatorNames.SaleEligibility;
        var sw = Stopwatch.StartNew();

        var memKey = $"mem:{SaleProjectionConstants.CacheKeys.CampaignMeta(campaignId)}";
        if (memoryCache.TryGetValue<CampaignSnapshot>(memKey, out var cached) && cached is not null)
        {
            RecordSource(validator, SaleProjectionConstants.Sources.MemoryHit);
            RecordDuration(validator, sw);
            return Result.Success<CampaignSnapshot?>(cached);
        }

        var snapshot = await redis.GetAsync<CampaignSnapshot>(
            SaleProjectionConstants.CacheKeys.CampaignMeta(campaignId), ct);

        if (snapshot is not null)
        {
            memoryCache.Set(memKey, snapshot, _memoryTtl);
            RecordSource(validator, SaleProjectionConstants.Sources.RedisHit);
            RecordDuration(validator, sw);
            return Result.Success<CampaignSnapshot?>(snapshot);
        }

        RecordSource(validator, SaleProjectionConstants.Sources.Miss);
        RecordDuration(validator, sw);
        return Result.Success<CampaignSnapshot?>(null);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, decimal>>> GetSalePricesAsync(
        Guid campaignId, CancellationToken ct)
    {
        var validator = SaleProjectionConstants.ValidatorNames.SalePricing;
        var sw = Stopwatch.StartNew();

        var memKey = $"mem:{SaleProjectionConstants.CacheKeys.CampaignPrices(campaignId)}";
        if (memoryCache.TryGetValue<IReadOnlyDictionary<Guid, decimal>>(memKey, out var cached) && cached is not null)
        {
            RecordSource(validator, SaleProjectionConstants.Sources.MemoryHit);
            RecordDuration(validator, sw);
            return Result.Success(cached);
        }

        var prices = await redis.GetAsync<Dictionary<Guid, decimal>>(
            SaleProjectionConstants.CacheKeys.CampaignPrices(campaignId), ct);

        if (prices is { Count: > 0 })
        {
            IReadOnlyDictionary<Guid, decimal> ro = prices;
            memoryCache.Set(memKey, ro, _memoryTtl);
            RecordSource(validator, SaleProjectionConstants.Sources.RedisHit);
            RecordDuration(validator, sw);
            return Result.Success(ro);
        }

        RecordSource(validator, SaleProjectionConstants.Sources.Miss);
        RecordDuration(validator, sw);
        return Result.Success<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private static void RecordSource(string validator, string source) =>
        OrderValidatorMetrics.ValidatorSource.Add(1,
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Validator, validator),
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Source, source));

    private static void RecordDuration(string validator, Stopwatch sw)
    {
        sw.Stop();
        OrderValidatorMetrics.ValidatorDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Validator, validator));
    }
}
