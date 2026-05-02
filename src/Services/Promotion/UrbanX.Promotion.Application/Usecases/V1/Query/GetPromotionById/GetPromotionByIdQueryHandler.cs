using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Usecases.V1.Errors;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

internal sealed class GetPromotionByIdQueryHandler(IPromotionRepository promotionRepository)
    : IQueryHandler<GetPromotionByIdQuery, PromotionDetailDto>
{
    public async Task<Result<PromotionDetailDto>> Handle(GetPromotionByIdQuery query, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(query.Id, ct);
        if (promotion is null)
            return Result.Failure<PromotionDetailDto>(PromotionErrors.NotFound(query.Id));

        return Result.Success(new PromotionDetailDto(
            promotion.Id,
            promotion.Name,
            promotion.Description,
            promotion.Type,
            promotion.DiscountType,
            promotion.DiscountValue,
            promotion.MaxDiscountCap,
            promotion.MinOrderAmount,
            promotion.StartsAt,
            promotion.EndsAt,
            promotion.MaxTotalUsages,
            promotion.MaxUsagesPerCustomer,
            promotion.UsageCount,
            promotion.Status,
            promotion.TargetScope,
            promotion.TargetIds,
            promotion.IsStackable,
            promotion.Codes.Select(c => new VoucherCodeDto(c.Id, c.Code, c.Status, c.AssignedToCustomerId)).ToList(),
            promotion.FlashSaleItems.Select(f => new FlashSaleItemDto(f.Id, f.ProductId, f.VariantId, f.TotalSlots, f.SlotsReserved)).ToList()
        ));
    }
}
