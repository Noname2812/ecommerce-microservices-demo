using Microsoft.Extensions.Options;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductVariantAdded;

public sealed class ProductVariantAddedProjectionConsumerDefinition(
    IOptions<CatalogProjectionConsumerOptions> options)
    : CatalogProjectionConsumerDefinitionBase<ProductVariantAddedProjectionConsumer>(options);
