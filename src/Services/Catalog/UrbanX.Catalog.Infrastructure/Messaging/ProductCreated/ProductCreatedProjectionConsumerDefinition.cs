using Microsoft.Extensions.Options;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Catalog.Infrastructure.Messaging.ProductCreated;

public sealed class ProductCreatedProjectionConsumerDefinition(
    IOptions<CatalogProjectionConsumerOptions> options)
    : CatalogProjectionConsumerDefinitionBase<ProductCreatedProjectionConsumer>(options);
