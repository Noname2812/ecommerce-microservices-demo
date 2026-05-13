using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Persistence.Seeding;

/// <summary>
/// Dev/test catalog seed: category, brand, and 10 active products with one variant each.
/// Product and variant IDs match the seed in the Inventory service so stock lines align.
/// </summary>
public static class CatalogDataSeeder
{
    /// <summary>Stable seller used only for seeded products (not tied to Identity users).</summary>
    public static readonly Guid SeedSellerId = Guid.Parse("BBBBBBBB-BBBB-4BBB-8BBB-BBBBBBBBBBBB");

    private static readonly Guid SeedCategoryId = Guid.Parse("CCCCCCCC-CCCC-4CCC-8CCC-CCCCCCCCCCC1");
    private static readonly Guid SeedBrandId = Guid.Parse("CCCCCCCC-CCCC-4CCC-8CCC-CCCCCCCCCCC2");

    /// <summary>Same ID formula as inventory seed (product).</summary>
    public static Guid SeedProductId(int n) => Guid.Parse($"{n:X8}-0000-4000-8000-000000000001");

    /// <summary>Same ID formula as inventory seed (variant).</summary>
    public static Guid SeedVariantId(int n) => Guid.Parse($"{n:X8}-0000-4000-8000-000000000002");

    public static async Task SeedIfEmptyAsync(CatalogDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Products.AnyAsync(cancellationToken))
            return;

        var utc = DateTimeOffset.UtcNow;

        var category = new Category
        {
            Id = SeedCategoryId,
            ParentId = null,
            Name = "Electronics",
            Slug = "electronic",
            Description = "Seed category",
            ImageUrl = null,
            DisplayOrder = 0,
            IsActive = true,
            Path = "/electronics",
            Depth = 0,
            CreatedAt = utc
        };

        var brand = new Brand
        {
            Id = SeedBrandId,
            Name = "UrbanX Seed",
            Slug = "urbanX-seed",
            LogoUrl = null,
            IsActive = true
        };

        context.Categories.Add(category);
        context.Brands.Add(brand);

        const string sellerName = "UrbanX Seed Seller";

        for (var n = 1; n <= 10; n++)
        {
            var name = $"Seed Product {n}";
            var price = 100_000m + n * 10_000m;
            var product = Product.Create(
                sku: $"SEED-PROD-{n:D2}",
                name: name,
                slug: name,
                description: "Seeded product for local development.",
                shortDescription: null,
                categoryId: SeedCategoryId,
                brandId: SeedBrandId,
                categoryName: category.Name,
                brandName: brand.Name,
                basePrice: price,
                sellerId: SeedSellerId,
                sellerName: sellerName,
                status: ProductStatus.Active,
                weightGrams: null,
                dimensions: null,
                tags: new List<string> { "seed", "demo" },
                metaTitle: null,
                metaDescription: null,
                productImages: Array.Empty<NewProductImageSpec>(),
                variantSpecs:
                [
                    new NewVariantSpec(
                        Sku: $"SEED-SKU-{n:D2}",
                        Name: $"Variant {n}",
                        Price: price,
                        CompareAtPrice: null,
                        ImageUrl: null,
                        Barcode: null,
                        AttributeValues: Array.Empty<(Guid AttributeId, string Value)>(),
                        GalleryImages: Array.Empty<NewProductImageSpec>(),
                        VariantId: SeedVariantId(n))
                ],
                productId: SeedProductId(n));

            context.Products.Add(product);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
