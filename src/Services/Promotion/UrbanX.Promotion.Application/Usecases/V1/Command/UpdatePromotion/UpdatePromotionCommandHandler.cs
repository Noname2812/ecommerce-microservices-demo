using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class UpdatePromotionCommandHandler(IPromotionRepository promotionRepository)
    : ICommandHandler<UpdatePromotionCommand>
{
    public async Task<Result> Handle(UpdatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(PromotionErrors.NotFound(cmd.Id));

        if (promotion.Status != PromotionStatus.Draft)
            return Result.Failure(PromotionErrors.CannotModify);

        promotion.Name = cmd.Name;
        promotion.Description = cmd.Description;
        promotion.DiscountValue = cmd.DiscountValue;
        promotion.MaxDiscountCap = cmd.MaxDiscountCap;
        promotion.MinOrderAmount = cmd.MinOrderAmount;
        promotion.StartsAt = cmd.StartsAt;
        promotion.EndsAt = cmd.EndsAt;
        promotion.MaxTotalUsages = cmd.MaxTotalUsages;
        promotion.MaxUsagesPerCustomer = cmd.MaxUsagesPerCustomer;
        promotion.TargetScope = cmd.TargetScope;
        promotion.TargetIds = cmd.TargetIds?.ToList() ?? [];
        promotion.IsStackable = cmd.IsStackable;

        return Result.Success();
    }
}
