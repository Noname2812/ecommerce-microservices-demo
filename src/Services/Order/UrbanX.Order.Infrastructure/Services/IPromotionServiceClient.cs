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

public record CampaignEligibilityResponse(bool Eligible, string? Reason);

/// <summary>Variant id + client line unit price (used by dev stub; real service maps variant ids to campaign prices).</summary>
public record PromotionSalePriceLine(Guid VariantId, decimal UnitPrice);

public interface IPromotionServiceClient
{
    Task<Result<PromotionRedeemResponse>> RedeemAsync(PromotionRedeemRequest request, CancellationToken ct = default);

    Task<Result<CampaignEligibilityResponse>> CheckCampaignEligibilityAsync(
        Guid campaignId, Guid userId, int totalQty, CancellationToken ct);

    Task<Result<Dictionary<Guid, decimal>>> GetSalePricesAsync(
        Guid campaignId, IReadOnlyList<PromotionSalePriceLine> lines, CancellationToken ct);
}
