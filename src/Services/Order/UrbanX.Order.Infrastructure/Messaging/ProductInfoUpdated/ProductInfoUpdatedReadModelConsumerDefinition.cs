using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductInfoUpdated;

public sealed class ProductInfoUpdatedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductInfoUpdatedReadModelConsumer>
{
    public ProductInfoUpdatedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
