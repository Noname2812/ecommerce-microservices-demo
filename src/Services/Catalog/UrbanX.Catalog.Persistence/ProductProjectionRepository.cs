using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Projections;

namespace UrbanX.Catalog.Persistence;

public sealed class ProductProjectionRepository(CatalogDbContext db) : IProductProjectionRepository
{
    public async Task UpsertAsync(Product product, CancellationToken ct = default)
    {
        var (listView, detailView) = ProductProjectionBuilder.Build(product);

        var existingVersion = await db.ProductListViews
            .AsNoTracking()
            .Where(r => r.ProductId == product.Id)
            .Select(r => (int?)r.ProjectionVersion)
            .FirstOrDefaultAsync(ct);

        listView.ProjectionVersion = detailView.ProjectionVersion = (existingVersion ?? 0) + 1;

        if (existingVersion is null)
        {
            db.ProductListViews.Add(listView);
            db.ProductDetailViews.Add(detailView);
        }
        else
        {
            db.ProductListViews.Update(listView);
            db.ProductDetailViews.Update(detailView);
        }
        // SaveChanges is handled by TransactionPipelineBehavior (via IUnitOfWork)
    }
}
