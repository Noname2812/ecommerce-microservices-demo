using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductCreated;

public sealed class ProductCreatedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductCreatedReadModelConsumer>
{
    public ProductCreatedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
