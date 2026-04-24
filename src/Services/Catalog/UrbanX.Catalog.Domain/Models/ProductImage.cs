using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models
{
    /// <summary>Gallery row: product-level and/or variant-level (see variant_id).</summary>
    public class ProductImage : BaseEntity<Guid>
    {
        public Guid ProductId { get; init; }
        public Guid? VariantId { get; init; }
        public string Url { get; init; } = null!;
        public string? AltText { get; init; }
        public int DisplayOrder { get; init; }
        public bool IsPrimary { get; init; }

        public Product Product { get; init; } = null!;
        public ProductVariant? Variant { get; init; }
    }
}
