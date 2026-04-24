using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Domain
{
    public interface IProductRepository
    {
        /// <summary>By id, excluding soft-deleted catalog rows. For read use cases.</summary>
        Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Tracked product graph for an edit transaction. Returns null if missing or not editable.</summary>
        Task<Product?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

        Task<bool> SkuInUseAsync(string sku, CancellationToken cancellationToken = default);

        /// <summary>True if <paramref name="sku"/> is on another product or another variant, excluding the given product/variant pair.</summary>
        Task<bool> IsSkuInUseExcludingAsync(
            string sku,
            Guid forProductId,
            Guid? forVariantId,
            CancellationToken cancellationToken = default);

        Task<bool> SlugInUseAsync(string slug, CancellationToken cancellationToken = default);

        Task<bool> IsSlugInUseExcludingProductAsync(
            string slug,
            Guid productId,
            CancellationToken cancellationToken = default);

        Task AddAsync(Product product, CancellationToken cancellationToken = default);
        Task AddPriceHistoryAsync(VariantPriceHistory row, CancellationToken cancellationToken = default);
        Task AddSkuHistoryAsync(VariantSkuHistory row, CancellationToken cancellationToken = default);
    }
}
