using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class AddFlashSaleItemsCommandHandler(IPromotionRepository promotionRepository)
    : ICommandHandler<AddFlashSaleItemsCommand>
{
    public async Task<Result> Handle(AddFlashSaleItemsCommand cmd, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(cmd.PromotionId, ct);
        if (promotion is null)
            return Result.Failure(PromotionErrors.NotFound(cmd.PromotionId));

        if (promotion.Type != PromotionType.FlashSale)
            return Result.Failure(PromotionErrors.InvalidPromotionType);

        var newItems = cmd.Items
            .Select(i => FlashSaleItem.Create(promotion.Id, i.ProductId, i.VariantId, i.TotalSlots))
            .ToList();

        promotion.AddFlashSaleItems(newItems);

        return Result.Success();
    }
}
