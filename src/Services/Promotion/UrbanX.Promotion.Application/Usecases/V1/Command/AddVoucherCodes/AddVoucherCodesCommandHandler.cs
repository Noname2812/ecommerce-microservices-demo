using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class AddVoucherCodesCommandHandler(IPromotionRepository promotionRepository)
    : ICommandHandler<AddVoucherCodesCommand>
{
    public async Task<Result> Handle(AddVoucherCodesCommand cmd, CancellationToken ct)
    {
        var promotion = await promotionRepository.GetByIdAsync(cmd.PromotionId, ct);
        if (promotion is null)
            return Result.Failure(PromotionErrors.NotFound(cmd.PromotionId));

        if (promotion.Type != PromotionType.Voucher && promotion.Type != PromotionType.Coupon)
            return Result.Failure(PromotionErrors.InvalidPromotionType);

        var existingCodes = promotion.Codes.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate = cmd.Codes.FirstOrDefault(c => existingCodes.Contains(c.Code));
        if (duplicate is not null)
            return Result.Failure(PromotionErrors.CodeAlreadyUsed(duplicate.Code));

        var newCodes = cmd.Codes
            .Select(item => VoucherCode.Create(promotion.Id, item.Code, item.AssignedToCustomerId))
            .ToList();

        promotion.AddCodes(newCodes);

        return Result.Success();
    }
}
