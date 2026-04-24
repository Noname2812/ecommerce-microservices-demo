using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Persistence
{
    public class ProductRepository : IProductRepository
    {
        private readonly CatalogDbContext _db;

        public ProductRepository(CatalogDbContext db) => _db = db;

        public async Task AddAsync(Product product, CancellationToken cancellationToken = default) =>
            await _db.Products.AddAsync(product, cancellationToken);

        public async Task AddPriceHistoryAsync(VariantPriceHistory row, CancellationToken cancellationToken = default) =>
            await _db.VariantPriceHistories.AddAsync(row, cancellationToken);

        public async Task AddSkuHistoryAsync(VariantSkuHistory row, CancellationToken cancellationToken = default) =>
            await _db.VariantSkuHistories.AddAsync(row, cancellationToken);

        public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            await _db.Products
                .AsNoTracking()
                .Where(p => p.DeletedAt == null && p.Status != ProductStatus.Deleted)
                .Include(p => p.Variants)
                .ThenInclude(v => v.AttributeValues)
                .ThenInclude(a => a.AttributeDefinition)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        public async Task<Product?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            await _db.Products
                .Where(p => p.DeletedAt == null && p.Status != ProductStatus.Deleted)
                .Include(p => p.Variants)
                .ThenInclude(v => v.AttributeValues)
                .ThenInclude(a => a.AttributeDefinition)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        public async Task<bool> SkuInUseAsync(string sku, CancellationToken cancellationToken = default) =>
            await _db.Products.AnyAsync(p => p.Sku == sku, cancellationToken)
            || await _db.ProductVariants.AnyAsync(
                v => v.Sku == sku && v.DeletedAt == null, cancellationToken);

        public async Task<bool> IsSkuInUseExcludingAsync(
            string sku,
            Guid forProductId,
            Guid? forVariantId,
            CancellationToken cancellationToken = default)
        {
            if (await _db.Products.AnyAsync(p => p.Sku == sku && p.Id != forProductId, cancellationToken))
                return true;
            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == forProductId, cancellationToken);
            if (product is not null && product.Sku == sku && forVariantId is null)
                return true;
            if (forVariantId is null)
            {
                return await _db.ProductVariants.AnyAsync(
                    v => v.DeletedAt == null && v.Sku == sku, cancellationToken);
            }
            return await _db.ProductVariants.AnyAsync(
                v => v.DeletedAt == null && v.Sku == sku && v.Id != forVariantId.Value,
                cancellationToken);
        }

        public async Task<bool> SlugInUseAsync(string slug, CancellationToken cancellationToken = default) =>
            await _db.Products.AnyAsync(
                p => p.Slug == slug && p.DeletedAt == null, cancellationToken);

        public async Task<bool> IsSlugInUseExcludingProductAsync(
            string slug,
            Guid productId,
            CancellationToken cancellationToken = default) =>
            await _db.Products.AnyAsync(
                p => p.Slug == slug && p.Id != productId && p.DeletedAt == null, cancellationToken);
    }
}
