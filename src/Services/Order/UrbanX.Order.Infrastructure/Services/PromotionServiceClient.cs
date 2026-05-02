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

    private sealed record ProblemResponse(string? Type, string? Detail);
}
