using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Promotion;

public static class PromotionIntegrationEvents
{
    public record PromotionRedeemedV1(
        Guid PromotionId,
        Guid OrderId,
        Guid CustomerId,
        decimal DiscountAmount,
        string PromotionType,
        string? CouponCode,
        DateTimeOffset RedeemedAt
    ) : IntegrationEventBase
    {
        public override string Source => "promotion";
    }
}
