namespace UrbanX.Order.Application.Abstractions;

/// <summary>
/// Cache-TTL policy for order-ticket status reads. Infrastructure binds the underlying
/// <c>Order:TicketCache</c> options; handlers stay clean of <c>IOptions&lt;T&gt;</c> wiring.
/// </summary>
public interface IOrderStatusCachePolicy
{
    /// <summary>TTL applied once the order has reached a terminal status (Confirmed / Cancelled).</summary>
    TimeSpan TerminalTtl { get; }

    /// <summary>TTL applied while the order is still in progress (polling window).</summary>
    TimeSpan NonTerminalTtl { get; }
}
