using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

internal sealed class ListPromotionsQueryHandler(IPromotionRepository promotionRepository)
    : IQueryHandler<ListPromotionsQuery, PageResult<PromotionListItemDto>>
{
    public async Task<Result<PageResult<PromotionListItemDto>>> Handle(ListPromotionsQuery query, CancellationToken ct)
    {
        var (items, totalCount) = await promotionRepository.ListAsync(
            query.Type, query.Status, query.PageIndex, query.PageSize, ct);

        var dtos = items.Select(p => new PromotionListItemDto(
            p.Id, p.Name, p.Type, p.DiscountType, p.DiscountValue,
            p.Status, p.StartsAt, p.EndsAt, p.UsageCount, p.MaxTotalUsages
        )).ToList();

        return Result.Success(PageResult<PromotionListItemDto>.Create(dtos, query.PageIndex, query.PageSize, totalCount));
    }
}
