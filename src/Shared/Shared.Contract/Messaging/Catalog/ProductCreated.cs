using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Catalog;

namespace Shared.Contract.Messaging.Catalog
{
    public static class ProductIntegrationEvents
    {
        public record ProductCreatedV1(
            Guid ProductId,
            string Sku,
            string Name,
            string Slug,
            string? Description,
            string? ShortDescription,
            Guid? CategoryId,
            Guid? BrandId,
            string? CategoryName,
            string? BrandName,
            decimal BasePrice,
            Guid SellerId,
            string SellerName,
            string Status,
            IReadOnlyList<string>? Tags,
            ProductDtos.ProductDimensionsSnapshot? Dimensions,
            IReadOnlyList<ProductDtos.ProductVariantSnapshot> Variants
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }
    }
}
