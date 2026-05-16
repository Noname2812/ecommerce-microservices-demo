using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record RedeemSalePromotionRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required Guid CampaignId { get; init; }
    public string? CouponCode { get; init; }
    public required decimal Subtotal { get; init; }
    public required IReadOnlyList<PromotionOrderItem> Items { get; init; }
}

public record PromotionRedeemedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required decimal OrderLevelDiscount { get; init; }
    public required IReadOnlyList<PromotionItemDiscount> ItemDiscounts { get; init; }
    public required IReadOnlyList<Guid> AppliedPromotionIds { get; init; }
    public required IReadOnlyList<ClaimedFlashSaleSlot> ClaimedFlashSaleSlots { get; init; }
}

public record PromotionRedeemFailedV1 : IntegrationEventBase
{
    public override string Source => "promotion-service";

    public required Guid OrderId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
}

public record PromotionOrderItem(Guid ProductId, Guid VariantId, int Quantity, decimal UnitPrice);

public record PromotionItemDiscount(Guid ProductId, Guid VariantId, decimal DiscountAmount);

public record ClaimedFlashSaleSlot(Guid PromotionId, string SlotKey, int Quantity);
