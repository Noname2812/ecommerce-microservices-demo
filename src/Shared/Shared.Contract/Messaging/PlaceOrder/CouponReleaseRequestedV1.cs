using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

/// <summary>
/// Concrete compensation payload for <see cref="ICouponReleaseRequested"/> (relay + Promotion consumer).
/// </summary>
public sealed record CouponReleaseRequestedV1 : IntegrationEventBase, ICouponReleaseRequested
{
    public required Guid ClaimId { get; init; }

    public required string Reason { get; init; }

    public override string Source => "order-service";

    string IPlaceOrderIntegrationContract.EventId => EventId.ToString();
}
