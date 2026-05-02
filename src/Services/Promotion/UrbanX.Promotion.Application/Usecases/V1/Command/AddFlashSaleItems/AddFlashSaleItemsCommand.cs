using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record AddFlashSaleItemsCommand(
    Guid PromotionId,
    IReadOnlyList<AddFlashSaleItemEntry> Items
) : ICommand;

public record AddFlashSaleItemEntry(Guid ProductId, Guid? VariantId, int TotalSlots);

public sealed class AddFlashSaleItemsCommandValidator : AbstractValidator<AddFlashSaleItemsCommand>
{
    public AddFlashSaleItemsCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.TotalSlots).GreaterThan(0);
        });
    }
}
