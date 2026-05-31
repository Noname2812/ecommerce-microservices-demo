using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public sealed record CouponReleaseRequestedV1 : IntegrationEventBase
{
    public required Guid ClaimId { get; init; }
    public required string Reason { get; init; }
    public override string Source => "order-service";
}
