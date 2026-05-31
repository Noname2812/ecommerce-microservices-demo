using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public sealed record OrderConfirmedForPlaceOrderV1 : IntegrationEventBase
{
    public required Guid OrderId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid ReservationId { get; init; }
    public Guid? ClaimId { get; init; }
    public required decimal FinalAmount { get; init; }
    public required DateTimeOffset ConfirmedAt { get; init; }
    public override string Source => "order-service";
}
