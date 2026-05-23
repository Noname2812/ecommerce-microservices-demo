using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.Order;

namespace UrbanX.Order.Infrastructure.Messaging.OrderCancelledCache;

/// <summary>
/// Invalidates the cached order-ticket status when an order reaches the Cancelled terminal state.
/// </summary>
public sealed class OrderCancelledCacheConsumer : IConsumer<OrderIntegrationEvents.OrderCancelledV1>
{
    private readonly ICacheService _cache;
    private readonly ILogger<OrderCancelledCacheConsumer> _logger;

    public OrderCancelledCacheConsumer(
        ICacheService cache,
        ILogger<OrderCancelledCacheConsumer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderIntegrationEvents.OrderCancelledV1> context)
    {
        var key = $"order:ticket:{context.Message.OrderId}";
        await _cache.RemoveAsync(key, context.CancellationToken);

        _logger.LogDebug(
            "Invalidated order-ticket cache key {CacheKey} for OrderId {OrderId}",
            key, context.Message.OrderId);
    }
}
