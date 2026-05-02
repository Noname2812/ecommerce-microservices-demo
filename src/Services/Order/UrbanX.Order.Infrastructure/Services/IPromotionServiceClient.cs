using Shared.Kernel.Primitives;

namespace UrbanX.Order.Infrastructure.Services;

public record PromotionRedeemRequest(
    Guid? OrderId,
    Guid CustomerId,
    string? CouponCode,
    decimal SubTotal,
    IReadOnlyList<PromotionRedeemItemDto> Items);

public record PromotionRedeemItemDto(Guid VariantId, Guid ProductId, int Quantity, decimal UnitPrice);

public record PromotionRedeemResponse(
    decimal OrderLevelDiscount,
    IReadOnlyList<PromotionItemDiscount> ItemDiscounts,
    IReadOnlyList<Guid> AppliedPromotionIds);

public record PromotionItemDiscount(Guid VariantId, decimal DiscountPerUnit);

public interface IPromotionServiceClient
{
    Task<Result<PromotionRedeemResponse>> RedeemAsync(PromotionRedeemRequest request, CancellationToken ct = default);
}
