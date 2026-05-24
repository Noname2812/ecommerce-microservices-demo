using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Messaging.ProductProjection;

namespace UrbanX.Order.Infrastructure.Messaging.ProductStatusChanged;

public sealed class ProductStatusChangedReadModelConsumerDefinition
    : ProductProjectionConsumerDefinitionBase<ProductStatusChangedReadModelConsumer>
{
    public ProductStatusChangedReadModelConsumerDefinition(IOptions<ProductProjectionConsumerOptions> options)
        : base(options)
    {
    }
}
