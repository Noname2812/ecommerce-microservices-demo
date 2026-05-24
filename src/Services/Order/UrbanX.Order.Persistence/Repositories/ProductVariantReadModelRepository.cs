using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Persistence.Repositories;

internal sealed class ProductVariantReadModelRepository(OrderDbContext db) : IProductVariantReadModelRepository
{
    public Task<ProductVariantReadModel?> GetByIdAsync(Guid variantId, CancellationToken ct = default) =>
        db.ProductVariants
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VariantId == variantId, ct);

    public async Task<IReadOnlyDictionary<Guid, ProductVariantReadModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken ct = default)
    {
        if (variantIds is null || variantIds.Count == 0)
            return new Dictionary<Guid, ProductVariantReadModel>();

        var rows = await db.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.VariantId))
            .ToListAsync(ct);

        return rows.ToDictionary(v => v.VariantId);
    }

    public async Task UpsertAsync(ProductVariantReadModel snapshot, CancellationToken ct = default)
    {
        var existing = await db.ProductVariants
            .FirstOrDefaultAsync(v => v.VariantId == snapshot.VariantId, ct);

        if (existing is null)
        {
            snapshot.ProjectionVersion = 1;
            db.ProductVariants.Add(snapshot);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Out-of-order event guard: skip if incoming RowVersion is older than current.
        if (existing.RowVersion > snapshot.RowVersion)
            return;

        existing.ProductId = snapshot.ProductId;
        existing.ProductName = snapshot.ProductName;
        existing.ProductIsActive = snapshot.ProductIsActive;
        existing.Sku = snapshot.Sku;
        existing.VariantName = snapshot.VariantName;
        existing.ImageUrl = snapshot.ImageUrl;
        existing.Price = snapshot.Price;
        existing.IsActive = snapshot.IsActive;
        existing.SellerId = snapshot.SellerId;
        existing.SellerName = snapshot.SellerName;
        existing.SellerIsActive = snapshot.SellerIsActive;
        existing.RowVersion = snapshot.RowVersion;
        existing.ProjectionVersion += 1;
        existing.UpdatedAt = snapshot.UpdatedAt;
        existing.DeletedAt = snapshot.DeletedAt;

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDeletedAsync(Guid variantId, DateTimeOffset deletedAt, CancellationToken ct = default)
    {
        var existing = await db.ProductVariants
            .FirstOrDefaultAsync(v => v.VariantId == variantId, ct);

        if (existing is null)
            return;

        existing.DeletedAt = deletedAt;
        existing.IsActive = false;
        existing.ProjectionVersion += 1;
        existing.UpdatedAt = deletedAt;

        await db.SaveChangesAsync(ct);
    }

    public Task<ProductVariantReadModel?> GetAnyByProductIdAsync(Guid productId, CancellationToken ct = default) =>
        db.ProductVariants
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ProductId == productId, ct);

    public async Task UpdateProductStatusAsync(
        Guid productId,
        bool isActive,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        var rows = await db.ProductVariants
            .Where(v => v.ProductId == productId)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return;

        foreach (var row in rows)
        {
            row.ProductIsActive = isActive;
            row.ProjectionVersion += 1;
            row.UpdatedAt = updatedAt;
        }

        await db.SaveChangesAsync(ct);
    }
}
