using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Promotions.Read)]
public record ListPromotionsQuery(
    string? Type,
    string? Status,
    int PageIndex = 1,
    int PageSize = 20
) : IQuery<PageResult<PromotionListItemDto>>;

public sealed class ListPromotionsQueryValidator : AbstractValidator<ListPromotionsQuery>
{
    public ListPromotionsQueryValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(PageResult<object>.UpperPageSize);
    }
}
