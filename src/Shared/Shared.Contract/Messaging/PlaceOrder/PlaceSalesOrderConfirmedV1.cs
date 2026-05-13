using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public record PlaceSalesOrderConfirmedV1 : IntegrationEventBase
{
    public override string Source => "order-service";
    public Guid OrderId { get; init; }
    public Guid UserId { get; init; }
    public Guid CampaignId { get; init; }
    public Guid ReservationId { get; init; }
    public Guid? ClaimId { get; init; }
    public decimal FinalAmount { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
}
