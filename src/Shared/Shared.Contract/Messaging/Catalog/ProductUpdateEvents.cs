using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Catalog;

namespace Shared.Contract.Messaging.Catalog
{
    /// <summary>Outbox + integration events for product updates (see UPDATE-PRODUCT design doc).</summary>
    public static class ProductUpdateIntegrationEvents
    {
        // Renamed from ProductCatalogUpdatedV1; now includes SellerId + ActiveVariants for search re-index
        public record ProductInfoUpdatedV1(
            Guid ProductId,
            Guid SellerId,
            ProductDtos.ProductUpdateSnapshot Snapshot,
            IReadOnlyList<ProductDtos.ProductVariantSnapshot> ActiveVariants
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        // Updated: uses ProductVariantSnapshot (includes Barcode, IsActive); added SellerId
        public record ProductVariantAddedV1(
            Guid ProductId,
            Guid SellerId,
            ProductDtos.ProductVariantSnapshot Variant
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        // New: replaces ProductVariantPriceUpdatedV1 + ProductVariantSkuChangedV1 + ProductVariantDisabledV1
        // Previous* fields are non-null only when that field actually changed
        public record ProductVariantUpdatedV1(
            Guid ProductId,
            Guid SellerId,
            Guid VariantId,
            string? PreviousSku,
            decimal? PreviousPrice,
            bool? PreviousIsActive,
            ProductDtos.ProductVariantSnapshot Variant
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductVariantDeletedV1(
            Guid ProductId,
            Guid VariantId,
            string Sku
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductStatusChangedV1(
            Guid ProductId,
            string OldStatus,
            string NewStatus,
            string? Reason,
            IReadOnlyList<Guid> AffectedVariantIds
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductDeletedV1(
            Guid ProductId,
            Guid? DeletedBy,
            IReadOnlyList<Guid> AffectedVariantIds
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }
    }
}
