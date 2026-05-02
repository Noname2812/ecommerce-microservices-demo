using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

internal sealed class PreviewDiscountQueryHandler(
    IPromotionRepository promotionRepository,
    IPromotionUsageRepository usageRepository)
    : IQueryHandler<PreviewDiscountQuery, PreviewDiscountResponse>
{
    public async Task<Result<PreviewDiscountResponse>> Handle(PreviewDiscountQuery query, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        decimal orderLevelDiscount = 0;
        string? ineligibleReason = null;
        Guid? codePromotionId = null;

        if (!string.IsNullOrWhiteSpace(query.CouponCode))
        {
            var promotion = await promotionRepository.GetByCodeAsync(query.CouponCode, ct);
            if (promotion is null)
            {
                return Result.Success(new PreviewDiscountResponse(false, 0, [], [], "Promotion code not found"));
            }

            if (promotion.Status != PromotionStatus.Active || promotion.StartsAt > now)
                ineligibleReason = "Promotion is not currently active";
            else if (promotion.EndsAt.HasValue && promotion.EndsAt <= now)
                ineligibleReason = "Promotion has expired";
            else if (promotion.MinOrderAmount.HasValue && query.Subtotal < promotion.MinOrderAmount.Value)
                ineligibleReason = $"Minimum order amount of {promotion.MinOrderAmount.Value:F2} required";
            else if (promotion.MaxTotalUsages.HasValue)
            {
                var totalUsage = await usageRepository.CountUsageAsync(promotion.Id, ct);
                if (totalUsage >= promotion.MaxTotalUsages.Value)
                    ineligibleReason = "Promotion usage limit reached";
            }

            if (ineligibleReason is null && promotion.MaxUsagesPerCustomer.HasValue)
            {
                var customerUsage = await usageRepository.CountCustomerUsageAsync(promotion.Id, query.CustomerId, ct);
                if (customerUsage >= promotion.MaxUsagesPerCustomer.Value)
                    ineligibleReason = "You have already used this promotion the maximum number of times";
            }

            if (ineligibleReason is null)
            {
                orderLevelDiscount = CalculateOrderDiscount(promotion, query.Subtotal);
                codePromotionId = promotion.Id;
            }
        }

        var variantIds = query.Items.Select(i => i.VariantId).ToList();
        var activeFlashSales = await promotionRepository.GetActiveFlashSalesForItemsAsync(variantIds, ct);

        var itemDiscounts = new List<ItemDiscount>();
        var applicableIds = new List<Guid>();
        if (codePromotionId.HasValue) applicableIds.Add(codePromotionId.Value);

        foreach (var flashPromotion in activeFlashSales)
        {
            foreach (var item in query.Items)
            {
                var flashItem = flashPromotion.FlashSaleItems.FirstOrDefault(f =>
                    f.VariantId == item.VariantId ||
                    (f.VariantId == null && f.ProductId == item.ProductId));

                if (flashItem is null) continue;

                var discountPerUnit = CalculateItemDiscount(flashPromotion, item.UnitPrice);
                itemDiscounts.Add(new ItemDiscount(item.VariantId, discountPerUnit));

                if (!applicableIds.Contains(flashPromotion.Id))
                    applicableIds.Add(flashPromotion.Id);
            }
        }

        var isEligible = ineligibleReason is null && (orderLevelDiscount > 0 || itemDiscounts.Count > 0);

        return Result.Success(new PreviewDiscountResponse(
            isEligible, orderLevelDiscount, itemDiscounts, applicableIds, ineligibleReason));
    }

    private static decimal CalculateOrderDiscount(Domain.Models.Promotion promotion, decimal subtotal) =>
        promotion.DiscountType switch
        {
            var t when t == DiscountType.FixedAmount => Math.Min(promotion.DiscountValue, subtotal),
            var t when t == DiscountType.Percentage => Math.Round(
                Math.Min(subtotal * promotion.DiscountValue / 100m, promotion.MaxDiscountCap ?? decimal.MaxValue), 2),
            _ => 0
        };

    private static decimal CalculateItemDiscount(Domain.Models.Promotion promotion, decimal unitPrice) =>
        promotion.DiscountType switch
        {
            var t when t == DiscountType.FixedAmount => Math.Min(promotion.DiscountValue, unitPrice),
            var t when t == DiscountType.Percentage => Math.Round(unitPrice * promotion.DiscountValue / 100m, 2),
            _ => 0
        };
}
