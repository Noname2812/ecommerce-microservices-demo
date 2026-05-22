using Microsoft.Extensions.Options;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductVariantDeleted;

public sealed class ProductVariantDeletedProjectionConsumerDefinition(
    IOptions<CatalogProjectionConsumerOptions> options)
    : CatalogProjectionConsumerDefinitionBase<ProductVariantDeletedProjectionConsumer>(options);
