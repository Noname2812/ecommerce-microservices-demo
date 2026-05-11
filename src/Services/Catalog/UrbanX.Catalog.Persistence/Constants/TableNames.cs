namespace UrbanX.Catalog.Persistence.Constants
{
    /// <summary>PostgreSQL table names (see database.md).</summary>
    internal static class TableNames
    {
        internal const string WriteSchema = "public";
        internal const string ReadSchema = "read";
        internal const string Categories = "categories";
        internal const string Brands = "brands";
        internal const string Products = "products";
        internal const string AttributeDefinitions = "attribute_definitions";
        internal const string ProductVariants = "product_variants";
        internal const string VariantAttributeValues = "variant_attribute_values";
        internal const string ProductImages = "product_images";
        internal const string VariantPriceHistory = "variant_price_history";
        internal const string VariantSkuHistory = "variant_sku_history";
        internal const string OutboxMessage = "outbox_messages";
        internal const string ProductListView = "product_list_view";
        internal const string ProductDetailView = "product_detail_view";
    }
}
