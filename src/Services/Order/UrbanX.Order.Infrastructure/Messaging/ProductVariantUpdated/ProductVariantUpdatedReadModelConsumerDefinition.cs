using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantUpdated;

public sealed class ProductVariantUpdatedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductVariantUpdatedReadModelConsumer>
{
    public ProductVariantUpdatedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
