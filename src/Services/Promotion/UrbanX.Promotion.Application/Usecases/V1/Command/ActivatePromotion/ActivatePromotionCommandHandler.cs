using Shared.Application;
using Shared.Cache.Abstractions;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Promotion.Application.Usecases.V1.Errors;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class ActivatePromotionCommandHandler(
    IPromotionRepository promotionRepository,
    ICacheService cacheService)
    : ICommandHandler<ActivatePromotionCommand>
{
    public async Task<Result> Handle(ActivatePromotionCommand cmd, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(cmd.Id, ct);
        if (promotion is null)
            return Result.Failure(PromotionErrors.NotFound(cmd.Id));

        if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Paused)
            return Result.Failure(PromotionErrors.CannotModify);

        promotion.Activate();

        if (promotion.Type == PromotionType.FlashSale)
            await SeedFlashSaleSlots(promotion, ct);

        return Result.Success();
    }

    private async Task SeedFlashSaleSlots(Domain.Models.Promotion promotion, CancellationToken ct)
    {
        var expiry = promotion.EndsAt.HasValue
            ? promotion.EndsAt.Value - DateTimeOffset.UtcNow
            : TimeSpan.FromDays(7);

        foreach (var item in promotion.FlashSaleItems)
        {
            var slotKey = item.VariantId ?? item.ProductId;
            var key = $"promotion:flash:{promotion.Id}:item:{slotKey}:slots";
            var remaining = item.TotalSlots - item.SlotsReserved;
            await cacheService.SetAsync(key, remaining, expiry, ct);
        }
    }
}
