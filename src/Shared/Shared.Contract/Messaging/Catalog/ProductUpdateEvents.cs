using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Catalog;

namespace Shared.Contract.Messaging.Catalog
{
    /// <summary>Outbox + integration events for product updates (see UPDATE-PRODUCT design doc).</summary>
    public static class ProductUpdateIntegrationEvents
    {
        public record ProductCatalogUpdatedV1(
            Guid ProductId,
            Guid SellerId,
            IReadOnlyDictionary<string, string?>? Changes,
            ProductDtos.ProductUpdateSnapshot FullSnapshot
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductVariantPriceUpdatedV1(
            Guid ProductId,
            IReadOnlyList<ProductDtos.VariantPriceChange> Variants
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductVariantAddedV1(
            Guid ProductId,
            ProductDtos.VariantSnapshot Variant
        ) : IntegrationEventBase
        {
            public override string Source => "catalog-service";
        }

        public record ProductVariantDisabledV1(
            Guid ProductId,
            Guid VariantId,
            string Sku,
            string Reason
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

        public record ProductVariantSkuChangedV1(
            Guid ProductId,
            Guid VariantId,
            string OldSku,
            string NewSku
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
