using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductVariantDeleted;

public sealed class ProductVariantDeletedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductVariantDeletedReadModelConsumer>
{
    public ProductVariantDeletedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
