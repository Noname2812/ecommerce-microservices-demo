namespace Shared.Contract.Dtos.Catalog
{
    public static class ProductDtos
    {
        public record ProductUpdateSnapshot(
            Guid Id,
            string Sku,
            string Name,
            string Slug,
            string? Description,
            string? ShortDescription,
            Guid? CategoryId,
            string? CategoryName,
            Guid? BrandId,
            string? BrandName,
            Guid SellerId,
            string? SellerName,
            decimal BasePrice,
            string Status,
            IReadOnlyList<string> Tags,
            int? WeightGrams,
            ProductDimensionsSnapshot? Dimensions,
            string? MetaTitle,
            string? MetaDescription,
            string? PrimaryImageUrl
        );

        public record VariantPriceChange(
            Guid VariantId,
            string Sku,
            decimal OldPrice,
            decimal NewPrice,
            decimal? OldCompareAtPrice,
            decimal? NewCompareAtPrice
        );

        public record VariantSnapshot(
            Guid VariantId,
            string Sku,
            string? Name,
            decimal Price,
            decimal? CompareAtPrice,
            IReadOnlyDictionary<string, string> Attributes,
            string? ImageUrl
        );

        public record ProductDimensionsSnapshot(
            decimal? LengthCm,
            decimal? WidthCm,
            decimal? HeightCm
        );

        public record ProductImageSnapshot(
            Guid ImageId,
            string Url,
            string? AltText,
            int DisplayOrder,
            bool IsPrimary,
            Guid? VariantId
        );

        public record ProductVariantAttributeSnapshot(
            string AttributeName,
            Guid AttributeDefinitionId,
            string Value
        );

        public record ProductVariantSnapshot(
            Guid VariantId,
            string Sku,
            string? Name,
            decimal Price,
            decimal? CompareAtPrice,
            string? ImageUrl,
            string? Barcode,
            bool IsActive,
            IReadOnlyList<ProductVariantAttributeSnapshot> AttributeValues,
            IReadOnlyList<string> ImageUrls,
            int RowVersion
        );
    }
}
