namespace UrbanX.Catalog.Domain.Models
{
    public class VariantAttributeValue
    {
        public Guid VariantId { get; set; }
        public Guid AttributeId { get; set; }
        public string Value { get; set; } = null!;

        public ProductVariant Variant { get; set; } = null!;
        public AttributeDefinition AttributeDefinition { get; set; } = null!;
    }
}
