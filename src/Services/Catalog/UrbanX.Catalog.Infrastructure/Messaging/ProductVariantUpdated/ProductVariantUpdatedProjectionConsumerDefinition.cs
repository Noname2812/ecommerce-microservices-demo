using Microsoft.Extensions.Options;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductVariantUpdated;

public sealed class ProductVariantUpdatedProjectionConsumerDefinition(
    IOptions<CatalogProjectionConsumerOptions> options)
    : CatalogProjectionConsumerDefinitionBase<ProductVariantUpdatedProjectionConsumer>(options);
