using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public record SaleQuotaReleaseRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";
    public Guid CampaignId { get; init; }
    public Guid UserId { get; init; }
    public int Quantity { get; init; }
    public string QuotaKey { get; init; } = null!;
}
