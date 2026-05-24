using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantAdded;

public sealed class ProductVariantAddedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductVariantAddedReadModelConsumer>
{
    public ProductVariantAddedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
