using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Persistence.Seeding;

/// <summary>
/// Dev/test seed for the product-variant read model. Mirrors the seed IDs used by
/// Catalog and Inventory so the same variant can be referenced across services
/// without waiting for the event pipeline to populate the projection.
/// </summary>
public static class OrderReadModelSeeder
{
    public static readonly Guid SeedSellerId = Guid.Parse("BBBBBBBB-BBBB-4BBB-8BBB-BBBBBBBBBBBB");

    public static Guid SeedProductId(int n) => Guid.Parse($"{n:X8}-0000-4000-8000-000000000001");

    public static Guid SeedVariantId(int n) => Guid.Parse($"{n:X8}-0000-4000-8000-000000000002");

    public static async Task SeedIfEmptyAsync(OrderDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.ProductVariants.AnyAsync(cancellationToken))
            return;

        var utc = DateTimeOffset.UtcNow;
        const string sellerName = "UrbanX Seed Seller";

        for (var n = 1; n <= 10; n++)
        {
            var productName = $"Điện thoại Product {n}";
            var price = 100_000m + n * 10_000m;

            context.ProductVariants.Add(new ProductVariantReadModel
            {
                VariantId = SeedVariantId(n),
                ProductId = SeedProductId(n),
                ProductName = productName,
                ProductIsActive = true,
                Sku = $"SEED-SKU-{n:D2}",
                VariantName = $"Variant {n}",
                ImageUrl = null,
                Price = price,
                IsActive = true,
                SellerId = SeedSellerId,
                SellerName = sellerName,
                SellerIsActive = true,
                RowVersion = 1,
                ProjectionVersion = 1,
                UpdatedAt = utc,
                DeletedAt = null
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
