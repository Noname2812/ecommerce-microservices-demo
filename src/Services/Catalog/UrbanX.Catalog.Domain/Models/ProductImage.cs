using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models
{
    /// <summary>Gallery row: product-level and/or variant-level (see variant_id).</summary>
    public class ProductImage : BaseEntity<Guid>
    {
        public Guid ProductId { get; set; }
        public Guid? VariantId { get; set; }
        public string Url { get; set; } = null!;
        public string? AltText { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsPrimary { get; set; }

        public Product Product { get; set; } = null!;
        public ProductVariant? Variant { get; set; }
    }
}
