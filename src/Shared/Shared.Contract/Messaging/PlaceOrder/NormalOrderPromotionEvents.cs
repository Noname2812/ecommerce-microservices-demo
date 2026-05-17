using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public record RedeemPromotionForNormalOrderRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required string CouponCode { get; init; }
    public required decimal Subtotal { get; init; }
    public required IReadOnlyList<NormalOrderPromotionItem> Items { get; init; }
}

public record NormalOrderPromotionRedeemedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required decimal OrderLevelDiscount { get; init; }
    public required IReadOnlyList<NormalOrderPromotionItemDiscount> ItemDiscounts { get; init; }
}

public record NormalOrderPromotionRedeemFailedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
}

public record NormalOrderPromotionItem(Guid ProductId, Guid VariantId, int Quantity, decimal UnitPrice);

public record NormalOrderPromotionItemDiscount(Guid ProductId, Guid VariantId, decimal DiscountAmount);
