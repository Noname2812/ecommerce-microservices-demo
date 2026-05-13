namespace UrbanX.Catalog.Domain.ReadModels;

public sealed class ProductListView
{
    public Guid ProductId { get; set; }
    public Guid SellerId { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public string? ShortDescription { get; set; }
    public decimal BasePrice { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int ProjectionVersion { get; set; }
    public string NameNormalized { get; set; } = string.Empty;
    public string SkuNormalized { get; set; } = string.Empty;
}
