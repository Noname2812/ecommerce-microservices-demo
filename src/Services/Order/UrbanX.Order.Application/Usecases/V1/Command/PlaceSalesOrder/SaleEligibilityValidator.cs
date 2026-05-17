using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Promotion;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

internal sealed class SaleEligibilityValidator(ISaleSnapshotCache snapshotCache)
    : ISaleEligibilityValidator
{
    public async Task<Result> ValidateAsync(
        Guid campaignId, Guid userId,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken ct)
    {
        var snapshotResult = await snapshotCache.GetCampaignAsync(campaignId, ct);
        if (snapshotResult.IsFailure)
            return Result.Failure(snapshotResult.Error);

        var campaign = snapshotResult.Value;
        if (campaign is null)
            return Result.Failure(OrderErrors.SaleCampaignInvalid("Campaign is not available"));

        if (!campaign.IsActive)
            return Result.Failure(OrderErrors.SaleCampaignInvalid("Campaign is not active"));

        var now = DateTimeOffset.UtcNow;
        if (now < campaign.StartsAt || now > campaign.EndsAt)
            return Result.Failure(OrderErrors.SaleCampaignInvalid("Campaign is outside the sale window"));

        // Per-user / global quota is enforced atomically by ISaleAllocationGate later in the handler.
        return Result.Success();
    }
}
