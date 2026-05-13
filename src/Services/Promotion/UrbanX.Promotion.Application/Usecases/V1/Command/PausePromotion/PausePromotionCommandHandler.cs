using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class PausePromotionCommandHandler(IPromotionRepository promotionRepository)
    : ICommandHandler<PausePromotionCommand>
{
    public async Task<Result> Handle(PausePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(PromotionErrors.NotFound(cmd.Id));

        if (promotion.Status != PromotionStatus.Active)
            return Result.Failure(PromotionErrors.NotActive);

        promotion.Pause();

        return Result.Success();
    }
}
