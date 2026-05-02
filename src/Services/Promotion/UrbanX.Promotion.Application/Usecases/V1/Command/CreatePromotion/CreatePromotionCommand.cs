using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record CreatePromotionCommand(
    string Name,
    string? Description,
    string Type,
    string DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountCap,
    decimal? MinOrderAmount,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? MaxTotalUsages,
    int? MaxUsagesPerCustomer,
    string TargetScope,
    IReadOnlyList<Guid>? TargetIds,
    bool IsStackable
) : ICommand<Guid>;

public sealed class CreatePromotionCommandValidator : AbstractValidator<CreatePromotionCommand>
{
    private static readonly string[] ValidTypes = [PromotionType.Voucher, PromotionType.Coupon, PromotionType.FlashSale];
    private static readonly string[] ValidDiscountTypes = [DiscountType.FixedAmount, DiscountType.Percentage, DiscountType.FreeShipping];
    private static readonly string[] ValidTargetScopes = [TargetScope.AllProducts, TargetScope.SpecificProducts, TargetScope.SpecificCategories];

    public CreatePromotionCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Type).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage($"Type must be one of: {string.Join(", ", ValidTypes)}");
        RuleFor(x => x.DiscountType).NotEmpty().Must(t => ValidDiscountTypes.Contains(t))
            .WithMessage($"DiscountType must be one of: {string.Join(", ", ValidDiscountTypes)}");
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.MaxDiscountCap).GreaterThan(0).When(x => x.MaxDiscountCap.HasValue);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue);
        RuleFor(x => x.StartsAt).NotEmpty();
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).When(x => x.EndsAt.HasValue);
        RuleFor(x => x.MaxTotalUsages).GreaterThan(0).When(x => x.MaxTotalUsages.HasValue);
        RuleFor(x => x.MaxUsagesPerCustomer).GreaterThan(0).When(x => x.MaxUsagesPerCustomer.HasValue);
        RuleFor(x => x.TargetScope).NotEmpty().Must(s => ValidTargetScopes.Contains(s))
            .WithMessage($"TargetScope must be one of: {string.Join(", ", ValidTargetScopes)}");
        RuleFor(x => x.TargetIds).NotEmpty()
            .When(x => x.TargetScope == TargetScope.SpecificProducts || x.TargetScope == TargetScope.SpecificCategories)
            .WithMessage("TargetIds are required when TargetScope is not ALL_PRODUCTS");
    }
}
