using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Messaging.OrderConfirmedCache;

public sealed class OrderConfirmedCacheConsumerDefinition
    : ConsumerDefinition<OrderConfirmedCacheConsumer>
{
    private readonly OrderTerminalStatusCacheConsumerOptions _options;

    public OrderConfirmedCacheConsumerDefinition(
        IOptions<OrderTerminalStatusCacheConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.ConfirmedQueueName))
            EndpointName = _options.ConfirmedQueueName;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderConfirmedCacheConsumer> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.RetryLimit > 0)
            endpointConfigurator.UseMessageRetry(r => r.Exponential(
                retry.RetryLimit,
                TimeSpan.FromMilliseconds(retry.MinIntervalMs),
                TimeSpan.FromMilliseconds(retry.MaxIntervalMs),
                TimeSpan.FromMilliseconds(retry.IntervalDeltaMs)));

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            if (_options.PrefetchCount is > 0)
                rabbit.PrefetchCount = _options.PrefetchCount.Value;

            if (_options.ConcurrentMessageLimit is > 0)
                rabbit.ConcurrentMessageLimit = _options.ConcurrentMessageLimit;
        }
    }
}
