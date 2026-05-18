using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// HTTP adapter for the Promotion service's flash-sale eligibility check.
///
/// <para>Expected Promotion-side contract (coordinated under <c>feat/promotion-redis-coordination</c>):</para>
/// <list type="bullet">
///   <item><c>POST /api/v1/promotion/campaigns/{campaignId}/validate</c></item>
///   <item>Body: <c>{ userId: Guid, items: [{ productId, variantId, quantity, unitPrice }] }</c></item>
///   <item>200 OK → <c>{ startAt, endAt, saleDiscountAmount, discountType }</c></item>
///   <item>400 Bad Request → <c>{ code: "SALE_EXPIRED" | "USER_ALREADY_BOUGHT" | "QUOTA_EXHAUSTED" | other, message }</c></item>
///   <item>404 Not Found → campaign does not exist</item>
/// </list>
///
/// Unknown error codes are surfaced to clients as <see cref="OrderErrors.SalePricingUnavailable"/>
/// (the raw code goes only to the warning log) so we never leak Promotion-internal taxonomy.
/// </summary>
internal sealed class PromotionSaleEligibilityClient(
    HttpClient httpClient,
    ILogger<PromotionSaleEligibilityClient> logger)
    : ISaleEligibilityService
{
    public async Task<Result<SaleEligibility>> ValidateAsync(
        Guid campaignId,
        Guid userId,
        IReadOnlyList<SaleEligibilityItem> items,
        CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"/api/v1/promotion/campaigns/{campaignId:D}/validate",
                new ValidateRequest(userId, items),
                ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<SaleEligibility>(OrderErrors.SaleCampaignInvalid("CAMPAIGN_NOT_FOUND"));

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await response.Content.ReadFromJsonAsync<ValidationErrorPayload>(ct);
                return MapPromotionError(error?.Code, campaignId, userId);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Promotion validate returned non-success {Status} for {CampaignId}/{UserId}",
                    response.StatusCode, campaignId, userId);
                return Result.Failure<SaleEligibility>(OrderErrors.SalePricingUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<EligibilityPayload>(ct);
            if (payload is null)
                return Result.Failure<SaleEligibility>(OrderErrors.SalePricingUnavailable);

            return Result.Success(new SaleEligibility(
                payload.StartAt,
                payload.EndAt,
                payload.SaleDiscountAmount,
                payload.DiscountType));
        }
        catch (Exception ex) when (IsPromotionTransportFailure(ex, ct))
        {
            logger.LogWarning(ex, "Promotion HTTP fallback failed (campaigns/validate) for {CampaignId}", campaignId);
            return Result.Failure<SaleEligibility>(OrderErrors.SalePricingUnavailable);
        }
    }

    private Result<SaleEligibility> MapPromotionError(string? code, Guid campaignId, Guid userId)
    {
        switch (code)
        {
            case "SALE_EXPIRED":
                return Result.Failure<SaleEligibility>(OrderErrors.SaleExpired);
            case "USER_ALREADY_BOUGHT":
                return Result.Failure<SaleEligibility>(OrderErrors.UserAlreadyBoughtFromSale);
            case "QUOTA_EXHAUSTED":
                return Result.Failure<SaleEligibility>(OrderErrors.SaleQuotaExceeded);
            default:
                // Don't leak unknown upstream codes to clients — log the raw value, return a
                // generic 503-class error so the API surface stays stable.
                logger.LogWarning(
                    "Promotion returned unknown error code {Code} for {CampaignId}/{UserId}",
                    code ?? "<null>", campaignId, userId);
                return Result.Failure<SaleEligibility>(OrderErrors.SalePricingUnavailable);
        }
    }

    private static bool IsPromotionTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
            return false;

        return ex is BrokenCircuitException
            or TimeoutRejectedException
            or HttpRequestException
            or TaskCanceledException;
    }

    private sealed record ValidateRequest(Guid UserId, IReadOnlyList<SaleEligibilityItem> Items);

    private sealed record EligibilityPayload(
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        decimal SaleDiscountAmount,
        string DiscountType);

    private sealed record ValidationErrorPayload(string Code, string? Message);
}
