using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Domain.Models;

/// <summary>Values applied to a product in a single edit transaction (set by application layer from API).</summary>
public sealed class ProductEditState
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public decimal BasePrice { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public int? WeightGrams { get; set; }
    public ProductDimensions? Dimensions { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? Status { get; set; }
}
