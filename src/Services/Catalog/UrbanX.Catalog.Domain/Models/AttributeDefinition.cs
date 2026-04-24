using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models
{
    /// <summary>Attribute schema row per category (e.g. Color, Size). Variant rows link in variant_attribute_values.</summary>
    public class AttributeDefinition : BaseEntity<Guid>
    {
        public Guid? CategoryId { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = "text";
        public bool IsVariantAttribute { get; set; }
        public int DisplayOrder { get; set; }

        public Category? Category { get; set; }
    }
}
