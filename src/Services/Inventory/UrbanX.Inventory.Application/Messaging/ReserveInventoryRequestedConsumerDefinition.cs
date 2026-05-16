using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Inventory.Application.DependencyInjection.Options;

namespace UrbanX.Inventory.Application.Messaging;

public sealed class ReserveInventoryRequestedConsumerDefinition
    : ConsumerDefinition<ReserveInventoryRequestedConsumer>
{
    private readonly ReserveInventoryRequestedConsumerOptions _options;

    public ReserveInventoryRequestedConsumerDefinition(
        IOptions<ReserveInventoryRequestedConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReserveInventoryRequestedConsumer> consumerConfigurator,
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
