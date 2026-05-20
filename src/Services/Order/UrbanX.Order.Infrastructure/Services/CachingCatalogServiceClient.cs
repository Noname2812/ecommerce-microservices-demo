using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// Redis-backed decorator over <see cref="CatalogServiceClient"/>. For each batched lookup it
/// hits Redis with MGET first; only missing ids hit the underlying HTTP client, and freshly
/// fetched rows are pipelined back into Redis under per-id keys.
///
/// <para>
/// Goal: when N concurrent place-order sagas request the same product/variant set, the catalog
/// HTTP fan-out collapses to one upstream call (the first miss) plus an MGET per saga. This both
/// reduces inter-service HTTP load and frees up the catalog DB connection pool.
/// </para>
///
/// <para>
/// Staleness: TTL of <see cref="CatalogClientCacheOptions.VariantTtlSeconds"/> means in-flight
/// orders may see slightly stale price/active flags. The saga downstream still re-validates
/// against inventory and catalog price-mismatch tolerance, so stale reads cannot cause oversell
/// or under-pricing — only an occasional saga compensation when staleness collides with a real
/// catalog mutation.
/// </para>
/// </summary>
internal sealed class CachingCatalogServiceClient(
    CatalogServiceClient inner,
    ICacheService cache,
    IOptions<CatalogClientCacheOptions> cacheOptions) : ICatalogServiceClient
{
    private const string VariantKeyPrefix = "catalog:variant:";
    private const string ProductKeyPrefix = "catalog:product:validation:";
    private const string PriceKeyPrefix = "catalog:variant:price:";

    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(cacheOptions.Value.VariantTtlSeconds);

    public async Task<Result<IReadOnlyList<CatalogVariantInfo>>> GetVariantsAsync(
        IEnumerable<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        var ids = variantIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return Result.Success<IReadOnlyList<CatalogVariantInfo>>(Array.Empty<CatalogVariantInfo>());

        var keys = ids.Select(id => VariantKeyPrefix + id.ToString("D")).ToArray();
        var cached = await cache.GetManyAsync<CatalogVariantInfo>(keys, cancellationToken);

        var hits = new List<CatalogVariantInfo>(ids.Length);
        var misses = new List<Guid>();
        for (var i = 0; i < ids.Length; i++)
        {
            if (cached[i] is not null) hits.Add(cached[i]!);
            else misses.Add(ids[i]);
        }

        if (misses.Count == 0)
            return Result.Success<IReadOnlyList<CatalogVariantInfo>>(hits);

        var fresh = await inner.GetVariantsAsync(misses, cancellationToken);
        if (fresh.IsFailure)
            return fresh;

        var toCache = fresh.Value!.ToDictionary(
            v => VariantKeyPrefix + v.VariantId.ToString("D"),
            v => v);
        await cache.SetManyAsync(toCache, _ttl, cancellationToken);

        hits.AddRange(fresh.Value);
        return Result.Success<IReadOnlyList<CatalogVariantInfo>>(hits);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>> ValidateProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(
                new Dictionary<Guid, CatalogProductValidationDto>());

        var keys = ids.Select(id => ProductKeyPrefix + id.ToString("D")).ToArray();
        var cached = await cache.GetManyAsync<CatalogProductValidationDto>(keys, cancellationToken);

        var result = new Dictionary<Guid, CatalogProductValidationDto>(ids.Length);
        var misses = new List<Guid>();
        for (var i = 0; i < ids.Length; i++)
        {
            if (cached[i] is not null) result[ids[i]] = cached[i]!;
            else misses.Add(ids[i]);
        }

        if (misses.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(result);

        var fresh = await inner.ValidateProductsAsync(misses, cancellationToken);
        if (fresh.IsFailure)
            return fresh;

        var toCache = fresh.Value!.ToDictionary(
            kv => ProductKeyPrefix + kv.Key.ToString("D"),
            kv => kv.Value);
        await cache.SetManyAsync(toCache, _ttl, cancellationToken);

        foreach (var (productId, dto) in fresh.Value)
            result[productId] = dto;

        return Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(result);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>> GetCurrentPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        var ids = variantIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(
                new Dictionary<Guid, CatalogPriceValidationDto>());

        var keys = ids.Select(id => PriceKeyPrefix + id.ToString("D")).ToArray();
        var cached = await cache.GetManyAsync<CatalogPriceValidationDto>(keys, cancellationToken);

        var result = new Dictionary<Guid, CatalogPriceValidationDto>(ids.Length);
        var misses = new List<Guid>();
        for (var i = 0; i < ids.Length; i++)
        {
            if (cached[i] is not null) result[ids[i]] = cached[i]!;
            else misses.Add(ids[i]);
        }

        if (misses.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(result);

        var fresh = await inner.GetCurrentPricesAsync(misses, cancellationToken);
        if (fresh.IsFailure)
            return fresh;

        var toCache = fresh.Value!.ToDictionary(
            kv => PriceKeyPrefix + kv.Key.ToString("D"),
            kv => kv.Value);
        await cache.SetManyAsync(toCache, _ttl, cancellationToken);

        foreach (var (variantId, dto) in fresh.Value)
            result[variantId] = dto;

        return Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(result);
    }
}
