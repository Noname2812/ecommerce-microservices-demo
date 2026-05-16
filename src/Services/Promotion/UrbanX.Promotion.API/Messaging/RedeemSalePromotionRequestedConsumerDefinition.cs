using MassTransit;
using MassTransit.RabbitMqTransport;
using Microsoft.Extensions.Options;
using UrbanX.Promotion.Application.DependencyInjection.Options;
using UrbanX.Promotion.Application.Messaging.Consumers;

namespace UrbanX.Promotion.API.Messaging;

public sealed class RedeemSalePromotionRequestedConsumerDefinition
    : ConsumerDefinition<RedeemSalePromotionRequestedConsumer>
{
    private readonly RedeemSalePromotionRequestedConsumerOptions _options;

    public RedeemSalePromotionRequestedConsumerDefinition(IOptions<RedeemSalePromotionRequestedConsumerOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.QueueName))
            Endpoint(e => e.Name = _options.QueueName!);
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<RedeemSalePromotionRequestedConsumer> consumerConfigurator,
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
