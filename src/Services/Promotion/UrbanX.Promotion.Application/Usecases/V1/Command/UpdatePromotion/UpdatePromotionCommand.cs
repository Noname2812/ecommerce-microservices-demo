using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record UpdatePromotionCommand(
    Guid Id,
    string Name,
    string? Description,
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
) : ICommand;

public sealed class UpdatePromotionCommandValidator : AbstractValidator<UpdatePromotionCommand>
{
    private static readonly string[] ValidTargetScopes = [TargetScope.AllProducts, TargetScope.SpecificProducts, TargetScope.SpecificCategories];

    public UpdatePromotionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.MaxDiscountCap).GreaterThan(0).When(x => x.MaxDiscountCap.HasValue);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue);
        RuleFor(x => x.StartsAt).NotEmpty();
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).When(x => x.EndsAt.HasValue);
        RuleFor(x => x.MaxTotalUsages).GreaterThan(0).When(x => x.MaxTotalUsages.HasValue);
        RuleFor(x => x.MaxUsagesPerCustomer).GreaterThan(0).When(x => x.MaxUsagesPerCustomer.HasValue);
        RuleFor(x => x.TargetScope).NotEmpty().Must(s => ValidTargetScopes.Contains(s));
        RuleFor(x => x.TargetIds).NotEmpty()
            .When(x => x.TargetScope == TargetScope.SpecificProducts || x.TargetScope == TargetScope.SpecificCategories);
    }
}
