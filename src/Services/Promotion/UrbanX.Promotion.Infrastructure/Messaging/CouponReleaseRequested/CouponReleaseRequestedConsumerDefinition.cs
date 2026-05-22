using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Promotion.Infrastructure.Messaging.CouponReleaseRequested;

/// <summary>
/// Binds the consumer queue to the fanout <c>compensation.events</c> exchange (see CompensationOutbox relay).
/// </summary>
public sealed class CouponReleaseRequestedConsumerDefinition
    : ConsumerDefinition<CouponReleaseRequestedConsumer>
{
    private readonly CouponReleaseRequestedConsumerOptions _options;

    public CouponReleaseRequestedConsumerDefinition(IOptions<CouponReleaseRequestedConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CouponReleaseRequestedConsumer> consumerConfigurator,
        IRegistrationContext _)
    {
        var retry = _options.Retry;
        if (retry.Intervals > 0 && retry.IntervalSeconds > 0)
        {
            endpointConfigurator.UseMessageRetry(r =>
                r.Interval(retry.Intervals, TimeSpan.FromSeconds(retry.IntervalSeconds)));
        }

        endpointConfigurator.ConfigureConsumeTopology = false;

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            if (_options.PrefetchCount is { } prefetch && prefetch > 0)
                rabbit.PrefetchCount = prefetch;

            if (_options.ConcurrentMessageLimit is > 0)
                rabbit.ConcurrentMessageLimit = _options.ConcurrentMessageLimit;

            rabbit.Bind("compensation.events", bind =>
            {
                bind.ExchangeType = "fanout";
            });
        }
    }
}
