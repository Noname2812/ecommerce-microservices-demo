using System.Net.Http.Json;
using Shared.Kernel.Primitives;

namespace UrbanX.Order.Infrastructure.Services;

internal sealed class PromotionServiceClient(HttpClient httpClient) : IPromotionServiceClient
{
    public async Task<Result<PromotionRedeemResponse>> RedeemAsync(PromotionRedeemRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/promotions/redeem", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(ct);
            return Result.Failure<PromotionRedeemResponse>(
                new Error(problem?.Type ?? "Promotion.Error", problem?.Detail ?? "Promotion validation failed"));
        }

        var result = await response.Content.ReadFromJsonAsync<PromotionRedeemResponse>(ct);
        return Result.Success(result!);
    }

    public async Task<Result<CampaignEligibilityResponse>> CheckCampaignEligibilityAsync(
        Guid campaignId, Guid userId, int totalQty, CancellationToken ct)
    {
        await Task.CompletedTask;
        return Result.Success(new CampaignEligibilityResponse(true, null));
    }

    public async Task<Result<Dictionary<Guid, decimal>>> GetSalePricesAsync(
        Guid campaignId, IReadOnlyList<PromotionSalePriceLine> lines, CancellationToken ct)
    {
        await Task.CompletedTask;
        // Dev stub: echo line prices per variant until Promotion HTTP exposes campaign prices.
        var dict = lines
            .GroupBy(l => l.VariantId)
            .ToDictionary(g => g.Key, g => g.Last().UnitPrice);
        return Result.Success(dict);
    }

    private sealed record ProblemResponse(string? Type, string? Detail);
}
