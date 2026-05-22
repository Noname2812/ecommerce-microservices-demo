using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Payment.Infrastructure.Messaging.OrderCancelled;

public sealed class OrderCancelledConsumerDefinition : ConsumerDefinition<OrderCancelledConsumer>
{
    private readonly OrderCancelledConsumerOptions _options;

    public OrderCancelledConsumerDefinition(IOptions<OrderCancelledConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderCancelledConsumer> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.RetryLimit > 0)
        {
            endpointConfigurator.UseMessageRetry(r =>
                r.Exponential(
                    retry.RetryLimit,
                    TimeSpan.FromMilliseconds(retry.MinIntervalMs),
                    TimeSpan.FromMilliseconds(retry.MaxIntervalMs),
                    TimeSpan.FromMilliseconds(retry.IntervalDeltaMs)));
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
