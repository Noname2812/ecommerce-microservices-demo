namespace Shared.Contract.Messaging.PlaceOrder;

/// <summary>
/// Base marker for place-order integration contracts; <see cref="EventId"/> supports consumer idempotency.
/// </summary>
public interface IPlaceOrderIntegrationContract
{
    /// <summary>Unique event instance id (typically a GUID string).</summary>
    string EventId { get; }
}
