using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.Order;

namespace UrbanX.Order.Infrastructure.Messaging.OrderConfirmedCache;

/// <summary>
/// Invalidates the cached order-ticket status when an order reaches the Confirmed terminal state.
/// Polling clients reading the same key (<c>order:ticket:{OrderId}</c>) will refetch fresh state on
/// the next request.
/// </summary>
public sealed class OrderConfirmedCacheConsumer : IConsumer<OrderConfirmedV1>
{
    private readonly ICacheService _cache;
    private readonly ILogger<OrderConfirmedCacheConsumer> _logger;

    public OrderConfirmedCacheConsumer(
        ICacheService cache,
        ILogger<OrderConfirmedCacheConsumer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderConfirmedV1> context)
    {
        var key = $"order:ticket:{context.Message.OrderId}";
        await _cache.RemoveAsync(key, context.CancellationToken);

        _logger.LogDebug(
            "Invalidated order-ticket cache key {CacheKey} for OrderId {OrderId}",
            key, context.Message.OrderId);
    }
}
