using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

[RequirePermission(Permissions.Promotions.Read)]
public record GetPromotionByIdQuery(Guid Id) : IQuery<PromotionDetailDto>;

public sealed class GetPromotionByIdQueryValidator : AbstractValidator<GetPromotionByIdQuery>
{
    public GetPromotionByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
