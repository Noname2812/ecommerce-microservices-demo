using System.Text.Json;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ReadModels;

namespace UrbanX.Catalog.Persistence.Projections;

internal static class ProductProjectionBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static (ProductListView List, ProductDetailView Detail) Build(Product product)
    {
        var primaryImage = product.Images
            .Where(i => i.VariantId == null)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.DisplayOrder)
            .FirstOrDefault()?.Url;

        var tags = product.Tags.ToArray();

        var listView = new ProductListView
        {
            ProductId = product.Id,
            SellerId = product.SellerId,
            Sku = product.Sku,
            Name = product.Name,
            Slug = product.Slug,
            Status = product.Status,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            BrandId = product.BrandId,
            BrandName = product.BrandName,
            ShortDescription = product.ShortDescription,
            BasePrice = product.BasePrice,
            PrimaryImageUrl = primaryImage,
            Tags = tags,
            UpdatedAt = product.UpdatedAt,
            DeletedAt = product.DeletedAt,
            ProjectionVersion = 1
        };

        var variants = product.Variants
            .Where(v => v.DeletedAt == null)
            .Select(v => new VariantProjection(
                v.Id, v.Sku, v.Name, v.Price, v.CompareAtPrice, v.ImageUrl, v.IsActive,
                v.AttributeValues
                    .Select(av => new AttributeProjection(av.AttributeDefinition?.Name ?? string.Empty, av.Value))
                    .ToList(),
                product.Images
                    .Where(i => i.VariantId == v.Id)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .ToList()))
            .ToList();

        var detailView = new ProductDetailView
        {
            ProductId = product.Id,
            SellerId = product.SellerId,
            Sku = product.Sku,
            Name = product.Name,
            Slug = product.Slug,
            Status = product.Status,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            BrandId = product.BrandId,
            BrandName = product.BrandName,
            ShortDescription = product.ShortDescription,
            BasePrice = product.BasePrice,
            PrimaryImageUrl = primaryImage,
            VariantsJson = JsonSerializer.Serialize(variants, JsonOpts),
            Tags = tags,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            WeightGrams = product.WeightGrams,
            DimensionsJson = product.Dimensions is not null
                ? JsonSerializer.Serialize(product.Dimensions, JsonOpts)
                : null,
            UpdatedAt = product.UpdatedAt,
            DeletedAt = product.DeletedAt,
            ProjectionVersion = 1
        };

        return (listView, detailView);
    }
}

file sealed record VariantProjection(
    Guid Id, string Sku, string? Name, decimal Price, decimal? CompareAtPrice,
    string? ImageUrl, bool IsActive,
    List<AttributeProjection> Attributes,
    List<string> GalleryImageUrls);

file sealed record AttributeProjection(string Name, string Value);
