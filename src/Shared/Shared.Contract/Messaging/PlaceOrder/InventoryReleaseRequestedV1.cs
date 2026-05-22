using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

/// <summary>
/// Concrete compensation payload for <see cref="IInventoryReleaseRequested"/> (relay + consumers).
/// </summary>
public sealed record InventoryReleaseRequestedV1 : IntegrationEventBase
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    public override string Source => "order-service";
}
