using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.Promotion;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using StackExchange.Redis;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class RedeemPromotionCommandHandler(
    IPromotionRepository promotionRepository,
    IPromotionUsageRepository usageRepository,
    ICacheService cacheService,
    IOutboxWriter outboxWriter)
    : ICommandHandler<RedeemPromotionCommand, RedeemPromotionResult>
{
    // Atomic slot claim: return 1=claimed, 0=sold out, -1=key not initialized
    private const string ClaimSlotScript = """
        local current = redis.call('GET', KEYS[1])
        if current == false then return -1 end
        local slots = tonumber(current)
        if slots > 0 then
          redis.call('DECR', KEYS[1])
          return 1
        else
          return 0
        end
        """;

    public async Task<Result<RedeemPromotionResult>> Handle(RedeemPromotionCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        decimal orderLevelDiscount = 0;
        Guid? appliedCodePromotionId = null;
        Guid? appliedVoucherCodeId = null;

        // Step 1: Handle coupon/voucher code
        if (!string.IsNullOrWhiteSpace(cmd.CouponCode))
        {
            var promotion = await promotionRepository.GetByCodeAsync(cmd.CouponCode, ct);
            if (promotion is null)
                return Result.Failure<RedeemPromotionResult>(PromotionErrors.CodeNotFound(cmd.CouponCode));

            var validationError = ValidatePromotion(promotion, cmd.Subtotal, now);
            if (validationError is not null)
                return Result.Failure<RedeemPromotionResult>(validationError);

            var usageLimitError = await CheckUsageLimits(promotion, cmd.CustomerId, ct);
            if (usageLimitError is not null)
                return Result.Failure<RedeemPromotionResult>(usageLimitError);

            if (promotion.Type == PromotionType.Voucher)
            {
                var upperCode = cmd.CouponCode.ToUpperInvariant();
                var voucherCode = promotion.Codes.FirstOrDefault(c => c.Code == upperCode);
                if (voucherCode is null || voucherCode.Status != VoucherCodeStatus.Active)
                    return Result.Failure<RedeemPromotionResult>(PromotionErrors.CodeAlreadyUsed(cmd.CouponCode));

                if (voucherCode.AssignedToCustomerId.HasValue && voucherCode.AssignedToCustomerId != cmd.CustomerId)
                    return Result.Failure<RedeemPromotionResult>(PromotionErrors.CodeNotFound(cmd.CouponCode));

                voucherCode.MarkAsUsed();
                appliedVoucherCodeId = voucherCode.Id;
            }

            orderLevelDiscount = CalculateOrderDiscount(promotion, cmd.Subtotal);
            appliedCodePromotionId = promotion.Id;
            promotion.IncrementUsage();

            var usage = PromotionUsage.Record(promotion.Id, appliedVoucherCodeId, cmd.OrderId, cmd.CustomerId, orderLevelDiscount);
            await usageRepository.AddAsync(usage, ct);

            await outboxWriter.WriteAsync(new PromotionIntegrationEvents.PromotionRedeemedV1(
                promotion.Id, cmd.OrderId, cmd.CustomerId, orderLevelDiscount,
                promotion.Type, cmd.CouponCode, now), ct);
        }

        // Step 2: Handle flash sale items
        var variantIds = cmd.Items.Select(i => i.VariantId).ToList();
        var activeFlashSales = await promotionRepository.GetActiveFlashSalesForItemsAsync(variantIds, ct);

        var itemDiscounts = new List<ItemDiscount>();
        var flashSalePromotionIds = new List<Guid>();
        var claimedSlots = new List<ClaimedFlashSaleSlotResult>();

        foreach (var flashPromotion in activeFlashSales)
        {
            foreach (var orderItem in cmd.Items)
            {
                var flashItem = flashPromotion.FlashSaleItems.FirstOrDefault(f =>
                    f.VariantId == orderItem.VariantId ||
                    (f.VariantId == null && f.ProductId == orderItem.ProductId));

                if (flashItem is null) continue;

                var slotKey = flashItem.VariantId ?? flashItem.ProductId;
                var redisKey = $"promotion:flash:{flashPromotion.Id}:item:{slotKey}:slots";
                var luaResult = await cacheService.EvalAsync(
                    ClaimSlotScript,
                    [new RedisKey(redisKey)],
                    null,
                    ct);

                var claimed = (long)luaResult;
                if (claimed <= 0) continue;

                var discountPerUnit = CalculateItemDiscount(flashPromotion, orderItem.UnitPrice);
                itemDiscounts.Add(new ItemDiscount(orderItem.VariantId, discountPerUnit));
                claimedSlots.Add(new ClaimedFlashSaleSlotResult(flashPromotion.Id, slotKey.ToString(), orderItem.Quantity));

                if (!flashSalePromotionIds.Contains(flashPromotion.Id))
                {
                    flashSalePromotionIds.Add(flashPromotion.Id);
                    flashPromotion.IncrementUsage();

                    var flashUsage = PromotionUsage.Record(
                        flashPromotion.Id, null, cmd.OrderId, cmd.CustomerId, discountPerUnit * orderItem.Quantity);
                    await usageRepository.AddAsync(flashUsage, ct);

                    await outboxWriter.WriteAsync(new PromotionIntegrationEvents.PromotionRedeemedV1(
                        flashPromotion.Id, cmd.OrderId, cmd.CustomerId, discountPerUnit * orderItem.Quantity,
                        flashPromotion.Type, null, now), ct);
                }
            }
        }

        var allAppliedIds = new List<Guid>();
        if (appliedCodePromotionId.HasValue) allAppliedIds.Add(appliedCodePromotionId.Value);
        allAppliedIds.AddRange(flashSalePromotionIds);

        return Result.Success(new RedeemPromotionResult(orderLevelDiscount, itemDiscounts, allAppliedIds, claimedSlots));
    }

    private static Error? ValidatePromotion(Domain.Models.Promotion promotion, decimal subtotal, DateTimeOffset now)
    {
        if (promotion.Status != PromotionStatus.Active)
            return PromotionErrors.NotActive;
        if (promotion.StartsAt > now)
            return PromotionErrors.NotStarted;
        if (promotion.EndsAt.HasValue && promotion.EndsAt <= now)
            return PromotionErrors.Expired;
        if (promotion.MinOrderAmount.HasValue && subtotal < promotion.MinOrderAmount.Value)
            return PromotionErrors.MinOrderAmountNotMet(promotion.MinOrderAmount.Value, subtotal);
        return null;
    }

    private async Task<Error?> CheckUsageLimits(Domain.Models.Promotion promotion, Guid customerId, CancellationToken ct)
    {
        if (promotion.MaxTotalUsages.HasValue)
        {
            var totalUsage = await usageRepository.CountUsageAsync(promotion.Id, ct);
            if (totalUsage >= promotion.MaxTotalUsages.Value)
                return PromotionErrors.UsageLimitReached;
        }

        if (promotion.MaxUsagesPerCustomer.HasValue)
        {
            var customerUsage = await usageRepository.CountCustomerUsageAsync(promotion.Id, customerId, ct);
            if (customerUsage >= promotion.MaxUsagesPerCustomer.Value)
                return PromotionErrors.CustomerUsageLimitReached;
        }

        return null;
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
