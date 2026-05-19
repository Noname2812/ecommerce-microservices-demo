using Microsoft.Extensions.Logging;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.Order;
using Shared.Messaging;

namespace UrbanX.Order.Application.Messaging;

public sealed class OrderConfirmedCacheConsumer(
    ICacheService cache,
    ILogger<OrderConfirmedCacheConsumer> logger)
    : IntegrationEventConsumerBase<OrderConfirmedV1, OrderConfirmedCacheConsumer>(logger)
{
    protected override Task HandleAsync(OrderConfirmedV1 @event, CancellationToken cancellationToken)
    {
        var key = $"order:ticket:{@event.OrderId}";
        return cache.RemoveAsync(key, cancellationToken);
    }
}

public sealed class OrderCancelledCacheConsumer(
    ICacheService cache,
    ILogger<OrderCancelledCacheConsumer> logger)
    : IntegrationEventConsumerBase<OrderIntegrationEvents.OrderCancelledV1, OrderCancelledCacheConsumer>(logger)
{
    protected override Task HandleAsync(
        OrderIntegrationEvents.OrderCancelledV1 @event,
        CancellationToken cancellationToken)
    {
        var key = $"order:ticket:{@event.OrderId}";
        return cache.RemoveAsync(key, cancellationToken);
    }
}
