using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Domain.Repositories;

public interface IProductVariantReadModelRepository
{
    Task<ProductVariantReadModel?> GetByIdAsync(Guid variantId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, ProductVariantReadModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken ct = default);

    Task UpsertAsync(ProductVariantReadModel snapshot, CancellationToken ct = default);

    Task MarkDeletedAsync(Guid variantId, DateTimeOffset deletedAt, CancellationToken ct = default);

    Task UpdateProductStatusAsync(Guid productId, bool isActive, DateTimeOffset updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Returns any existing variant row for the product so consumers handling variant-level events
    /// (<c>ProductVariantAddedV1</c>, <c>ProductVariantUpdatedV1</c>) can inherit denormalized
    /// product/seller info that the event payload itself does not carry.
    /// </summary>
    Task<ProductVariantReadModel?> GetAnyByProductIdAsync(Guid productId, CancellationToken ct = default);
}
