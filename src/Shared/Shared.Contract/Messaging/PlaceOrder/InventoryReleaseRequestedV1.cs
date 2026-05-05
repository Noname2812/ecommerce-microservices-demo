using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

/// <summary>
/// Concrete compensation payload for <see cref="IInventoryReleaseRequested"/> (relay + consumers).
/// </summary>
public sealed record InventoryReleaseRequestedV1 : IntegrationEventBase, IInventoryReleaseRequested
{
    public required Guid ReservationId { get; init; }

    public required string Reason { get; init; }

    /// <summary>Uses <see cref="IntegrationEventBase.CorrelationId"/> for wire serialization.</summary>
    string IInventoryReleaseRequested.CorrelationId => CorrelationId ?? string.Empty;

    public override string Source => "order-service";

    string IPlaceOrderIntegrationContract.EventId => EventId.ToString();
}
