using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Abstractions.Catalog;

/// <summary>
/// Tiered lookup for Catalog product/variant snapshots used by Place Order validators.
/// Implementations should:
///   L1: Redis cache (short TTL)
///   L2: Local read model populated via Catalog integration events (Phase B)
///   L3: Sync HTTP fallback to Catalog service (last resort)
/// </summary>
public interface IProductSnapshotCache
{
    Task<Result<IReadOnlyDictionary<Guid, ProductSnapshot>>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<Guid, VariantPriceSnapshot>>> GetVariantPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);

    Task InvalidateProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task InvalidateVariantAsync(Guid variantId, CancellationToken cancellationToken = default);
}
