using Microsoft.Extensions.Options;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Services;

internal sealed class OrderStatusCachePolicy : IOrderStatusCachePolicy
{
    private readonly OrderTicketCacheOptions _options;

    public OrderStatusCachePolicy(IOptions<OrderTicketCacheOptions> options)
        => _options = options.Value;

    public TimeSpan TerminalTtl => TimeSpan.FromSeconds(_options.TerminalTtlSeconds);
    public TimeSpan NonTerminalTtl => TimeSpan.FromSeconds(_options.NonTerminalTtlSeconds);
}
