using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class CreatePromotionCommandHandler(IPromotionRepository promotionRepository)
    : ICommandHandler<CreatePromotionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = Domain.Models.Promotion.Create(
            cmd.Name,
            cmd.Description,
            cmd.Type,
            cmd.DiscountType,
            cmd.DiscountValue,
            cmd.MaxDiscountCap,
            cmd.MinOrderAmount,
            cmd.StartsAt,
            cmd.EndsAt,
            cmd.MaxTotalUsages,
            cmd.MaxUsagesPerCustomer,
            cmd.TargetScope,
            cmd.TargetIds,
            cmd.IsStackable);

        await promotionRepository.AddAsync(promotion, ct);

        return Result.Success(promotion.Id);
    }
}
