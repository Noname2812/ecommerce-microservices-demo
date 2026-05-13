using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

internal sealed class SaleEligibilityValidator(IPromotionServiceClient promotionClient)
    : ISaleEligibilityValidator
{
    public async Task<Result> ValidateAsync(
        Guid campaignId, Guid userId,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken ct)
    {
        var totalQty = items.Sum(i => i.Quantity);
        var eligibilityResult = await promotionClient.CheckCampaignEligibilityAsync(campaignId, userId, totalQty, ct);

        if (eligibilityResult.IsFailure)
            return Result.Failure(eligibilityResult.Error);

        var response = eligibilityResult.Value!;
        if (!response.Eligible)
            return Result.Failure(OrderErrors.SaleCampaignInvalid(response.Reason ?? "Campaign is not eligible"));

        return Result.Success();
    }
}
