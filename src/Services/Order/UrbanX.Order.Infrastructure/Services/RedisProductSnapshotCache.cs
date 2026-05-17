using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Constants;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Application.Telemetry;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// 3-tier validator lookup:
///   L1: Redis cache (TTL from <see cref="CatalogSnapshotOptions.CacheTtlSeconds"/>)
///   L2: Local read model (Dapper, populated via Catalog integration events)
///   L3: Sync HTTP fallback to Catalog service (timeout from <see cref="CatalogSnapshotOptions.HttpFallbackTimeoutMilliseconds"/>)
/// </summary>
internal sealed class RedisProductSnapshotCache(
    ICacheService cache,
    ICatalogSnapshotReader snapshotReader,
    ICatalogServiceClient catalogClient,
    IOptions<CatalogSnapshotOptions> options,
    ILogger<RedisProductSnapshotCache> logger) : IProductSnapshotCache
{
    private readonly TimeSpan _snapshotTtl = TimeSpan.FromSeconds(options.Value.CacheTtlSeconds);

    public async Task<Result<IReadOnlyDictionary<Guid, ProductSnapshot>>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, ProductSnapshot>>(
                new Dictionary<Guid, ProductSnapshot>());

        var validator = CatalogProjectionConstants.ValidatorNames.Product;
        var sw = Stopwatch.StartNew();
        var resolved = new Dictionary<Guid, ProductSnapshot>(productIds.Count);
        var missingFromCache = new List<Guid>();

        // L1: Redis cache
        var cacheTasks = productIds.Select(async id =>
        {
            var snapshot = await cache.GetAsync<ProductSnapshot>(
                CatalogProjectionConstants.CacheKeys.Product(id), cancellationToken);
            return (id, snapshot);
        }).ToArray();

        var cacheResults = await Task.WhenAll(cacheTasks);
        foreach (var (id, snapshot) in cacheResults)
        {
            if (snapshot is not null) resolved[id] = snapshot;
            else missingFromCache.Add(id);
        }

        var cacheHits = productIds.Count - missingFromCache.Count;
        if (cacheHits > 0)
            RecordSource(validator, CatalogProjectionConstants.Sources.CacheHit, cacheHits);

        if (missingFromCache.Count == 0)
        {
            RecordDuration(validator, sw);
            return Result.Success<IReadOnlyDictionary<Guid, ProductSnapshot>>(resolved);
        }

        // L2: Local read model
        var localRows = await snapshotReader.GetByProductIdsAsync(missingFromCache, cancellationToken);
        var resolvedFromLocal = new List<(Guid id, ProductSnapshot snapshot)>(localRows.Count);
        var missingFromLocal = new List<Guid>();

        foreach (var id in missingFromCache)
        {
            if (localRows.TryGetValue(id, out var rows) && rows.Count > 0)
            {
                var snapshot = new ProductSnapshot(id, Exists: true, IsActive: rows[0].ProductIsActive, CachedAt: DateTime.UtcNow);
                resolved[id] = snapshot;
                resolvedFromLocal.Add((id, snapshot));
            }
            else
            {
                missingFromLocal.Add(id);
            }
        }

        if (resolvedFromLocal.Count > 0)
        {
            RecordSource(validator, CatalogProjectionConstants.Sources.LocalHit, resolvedFromLocal.Count);

            var warmTasks = resolvedFromLocal.Select(tuple =>
                cache.SetAsync(CatalogProjectionConstants.CacheKeys.Product(tuple.id),
                    tuple.snapshot, _snapshotTtl, cancellationToken));
            await Task.WhenAll(warmTasks);
        }

        if (missingFromLocal.Count == 0)
        {
            RecordDuration(validator, sw);
            return Result.Success<IReadOnlyDictionary<Guid, ProductSnapshot>>(resolved);
        }

        // L3: HTTP fallback
        logger.LogInformation(
            "Product snapshot HTTP fallback for {Count}/{Total} ids",
            missingFromLocal.Count, productIds.Count);

        var httpResult = await catalogClient.ValidateProductsAsync(missingFromLocal, cancellationToken);
        if (httpResult.IsFailure)
        {
            RecordSource(validator, CatalogProjectionConstants.Sources.Failed, missingFromLocal.Count);
            RecordDuration(validator, sw);
            return Result.Failure<IReadOnlyDictionary<Guid, ProductSnapshot>>(httpResult.Error);
        }

        RecordSource(validator, CatalogProjectionConstants.Sources.HttpFallback, missingFromLocal.Count);

        var now = DateTime.UtcNow;
        var fetched = httpResult.Value!;
        var setTasks = new List<Task>(fetched.Count);
        foreach (var id in missingFromLocal)
        {
            if (!fetched.TryGetValue(id, out var dto))
            {
                resolved[id] = new ProductSnapshot(id, Exists: false, IsActive: false, CachedAt: now);
                continue;
            }

            var snapshot = new ProductSnapshot(dto.ProductId, dto.Exists, dto.IsActive, now);
            resolved[id] = snapshot;
            setTasks.Add(cache.SetAsync(CatalogProjectionConstants.CacheKeys.Product(id),
                snapshot, _snapshotTtl, cancellationToken));
        }

        if (setTasks.Count > 0)
            await Task.WhenAll(setTasks);

        RecordDuration(validator, sw);
        return Result.Success<IReadOnlyDictionary<Guid, ProductSnapshot>>(resolved);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>> GetVariantPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        if (variantIds.Count == 0)
            return Result.Success<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>(
                new Dictionary<Guid, VariantPriceSnapshot>());

        var validator = CatalogProjectionConstants.ValidatorNames.Pricing;
        var sw = Stopwatch.StartNew();
        var resolved = new Dictionary<Guid, VariantPriceSnapshot>(variantIds.Count);
        var missingFromCache = new List<Guid>();

        // L1: Redis cache
        var cacheTasks = variantIds.Select(async id =>
        {
            var snapshot = await cache.GetAsync<VariantPriceSnapshot>(
                CatalogProjectionConstants.CacheKeys.Variant(id), cancellationToken);
            return (id, snapshot);
        }).ToArray();

        var cacheResults = await Task.WhenAll(cacheTasks);
        foreach (var (id, snapshot) in cacheResults)
        {
            if (snapshot is not null) resolved[id] = snapshot;
            else missingFromCache.Add(id);
        }

        var cacheHits = variantIds.Count - missingFromCache.Count;
        if (cacheHits > 0)
            RecordSource(validator, CatalogProjectionConstants.Sources.CacheHit, cacheHits);

        if (missingFromCache.Count == 0)
        {
            RecordDuration(validator, sw);
            return Result.Success<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>(resolved);
        }

        // L2: Local read model
        var localRows = await snapshotReader.GetByVariantIdsAsync(missingFromCache, cancellationToken);
        var resolvedFromLocal = new List<(Guid id, VariantPriceSnapshot snapshot)>(localRows.Count);
        var missingFromLocal = new List<Guid>();

        foreach (var id in missingFromCache)
        {
            if (localRows.TryGetValue(id, out var row))
            {
                var snapshot = new VariantPriceSnapshot(id, row.CurrentPrice, DateTime.UtcNow);
                resolved[id] = snapshot;
                resolvedFromLocal.Add((id, snapshot));
            }
            else
            {
                missingFromLocal.Add(id);
            }
        }

        if (resolvedFromLocal.Count > 0)
        {
            RecordSource(validator, CatalogProjectionConstants.Sources.LocalHit, resolvedFromLocal.Count);

            var warmTasks = resolvedFromLocal.Select(tuple =>
                cache.SetAsync(CatalogProjectionConstants.CacheKeys.Variant(tuple.id),
                    tuple.snapshot, _snapshotTtl, cancellationToken));
            await Task.WhenAll(warmTasks);
        }

        if (missingFromLocal.Count == 0)
        {
            RecordDuration(validator, sw);
            return Result.Success<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>(resolved);
        }

        // L3: HTTP fallback
        logger.LogInformation(
            "Variant price snapshot HTTP fallback for {Count}/{Total} ids",
            missingFromLocal.Count, variantIds.Count);

        var httpResult = await catalogClient.GetCurrentPricesAsync(missingFromLocal, cancellationToken);
        if (httpResult.IsFailure)
        {
            RecordSource(validator, CatalogProjectionConstants.Sources.Failed, missingFromLocal.Count);
            RecordDuration(validator, sw);
            return Result.Failure<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>(httpResult.Error);
        }

        RecordSource(validator, CatalogProjectionConstants.Sources.HttpFallback, missingFromLocal.Count);

        var now = DateTime.UtcNow;
        var fetched = httpResult.Value!;
        var setTasks = new List<Task>(fetched.Count);
        foreach (var id in missingFromLocal)
        {
            if (!fetched.TryGetValue(id, out var dto))
                continue;

            var snapshot = new VariantPriceSnapshot(dto.VariantId, dto.CurrentPrice, now);
            resolved[id] = snapshot;
            setTasks.Add(cache.SetAsync(CatalogProjectionConstants.CacheKeys.Variant(id),
                snapshot, _snapshotTtl, cancellationToken));
        }

        if (setTasks.Count > 0)
            await Task.WhenAll(setTasks);

        RecordDuration(validator, sw);
        return Result.Success<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>(resolved);
    }

    public Task InvalidateProductAsync(Guid productId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CatalogProjectionConstants.CacheKeys.Product(productId), cancellationToken);

    public Task InvalidateVariantAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CatalogProjectionConstants.CacheKeys.Variant(variantId), cancellationToken);

    private static void RecordSource(string validator, string source, int count) =>
        OrderValidatorMetrics.ValidatorSource.Add(count,
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Validator, validator),
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Source, source));

    private static void RecordDuration(string validator, Stopwatch sw)
    {
        sw.Stop();
        OrderValidatorMetrics.ValidatorDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>(CatalogProjectionConstants.Tags.Validator, validator));
    }
}
