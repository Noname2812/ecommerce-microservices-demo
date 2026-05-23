using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Messaging.OrderCancelledCache;

public sealed class OrderCancelledCacheConsumerDefinition
    : ConsumerDefinition<OrderCancelledCacheConsumer>
{
    private readonly OrderTerminalStatusCacheConsumerOptions _options;

    public OrderCancelledCacheConsumerDefinition(
        IOptions<OrderTerminalStatusCacheConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.CancelledQueueName))
            EndpointName = _options.CancelledQueueName;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderCancelledCacheConsumer> consumerConfigurator,
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
