using Microsoft.Extensions.Options;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductInfoUpdated;

public sealed class ProductInfoUpdatedProjectionConsumerDefinition(
    IOptions<CatalogProjectionConsumerOptions> options)
    : CatalogProjectionConsumerDefinitionBase<ProductInfoUpdatedProjectionConsumer>(options);
