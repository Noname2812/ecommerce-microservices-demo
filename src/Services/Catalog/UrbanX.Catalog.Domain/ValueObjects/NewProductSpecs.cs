namespace UrbanX.Catalog.Domain.ValueObjects
{
    /// <summary>Product-level image row to insert (no variant).</summary>
    public record NewProductImageSpec(
        string Url,
        string? AltText,
        int DisplayOrder,
        bool IsPrimary
    );

    /// <summary>Data required to build one variant and its child rows (attributes, images).</summary>
    public record NewVariantSpec(
        string Sku,
        string? Name,
        decimal Price,
        decimal? CompareAtPrice,
        string? ImageUrl,
        string? Barcode,
        IReadOnlyList<(Guid AttributeId, string Value)> AttributeValues,
        IReadOnlyList<NewProductImageSpec> GalleryImages,
        Guid? VariantId = null);
}
