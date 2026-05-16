using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[AllowAnonymous]
public record RedeemPromotionCommand(
    string? CouponCode,
    Guid CustomerId,
    Guid OrderId,
    decimal Subtotal,
    IReadOnlyList<RedeemOrderItem> Items
) : ICommand<RedeemPromotionResult>;

public record RedeemOrderItem(Guid ProductId, Guid VariantId, decimal UnitPrice, int Quantity);

public record RedeemPromotionResult(
    decimal OrderLevelDiscount,
    IReadOnlyList<ItemDiscount> ItemDiscounts,
    IReadOnlyList<Guid> AppliedPromotionIds,
    IReadOnlyList<ClaimedFlashSaleSlotResult> ClaimedFlashSaleSlots
);

public record ItemDiscount(Guid VariantId, decimal DiscountPerUnit);

public record ClaimedFlashSaleSlotResult(Guid PromotionId, string SlotKey, int Quantity);

public sealed class RedeemPromotionCommandValidator : AbstractValidator<RedeemPromotionCommand>
{
    public RedeemPromotionCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Subtotal).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.CouponCode).MaximumLength(100).When(x => x.CouponCode is not null);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}
