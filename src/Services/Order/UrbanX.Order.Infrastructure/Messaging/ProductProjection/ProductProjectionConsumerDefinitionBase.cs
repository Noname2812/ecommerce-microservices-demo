using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Messaging.ProductProjection;

/// <summary>
/// Common retry/throughput wiring for the six Catalog → Order product-projection consumers.
/// All six share the same uniform workload (one upsert into <c>read.product_variant_view</c>),
/// so they read tuning from a single <see cref="ProductProjectionConsumerOptions"/> section.
/// </summary>
public abstract class ProductProjectionConsumerDefinitionBase<TConsumer>
    : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    private readonly ProductProjectionConsumerOptions _options;

    protected ProductProjectionConsumerDefinitionBase(IOptions<ProductProjectionConsumerOptions> options)
    {
        _options = options.Value;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.RetryLimit > 0)
        {
            endpointConfigurator.UseMessageRetry(r => r.Exponential(
                retryLimit: retry.RetryLimit,
                minInterval: TimeSpan.FromMilliseconds(retry.MinIntervalMs),
                maxInterval: TimeSpan.FromMilliseconds(retry.MaxIntervalMs),
                intervalDelta: TimeSpan.FromMilliseconds(retry.IntervalDeltaMs)));
        }

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            if (_options.PrefetchCount is { } prefetch && prefetch > 0)
                rabbit.PrefetchCount = prefetch;

            if (_options.ConcurrentMessageLimit is > 0)
                rabbit.ConcurrentMessageLimit = _options.ConcurrentMessageLimit;
        }
    }
}
