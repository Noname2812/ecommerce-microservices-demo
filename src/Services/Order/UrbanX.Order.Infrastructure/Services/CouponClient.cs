using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Refit;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Exceptions;
using UrbanX.Order.Infrastructure.RefitApi.Coupon;
using UrbanX.Order.Infrastructure.RefitApi.Coupon.Dtos;

namespace UrbanX.Order.Infrastructure.Services;

public sealed class CouponClient(ICouponApi api, ILogger<CouponClient> logger) : ICouponClient
{
    private const string InventoryReleaseReason = "COUPON_CLAIM_FAILED";

    public async Task<ClaimCouponResponse> ClaimAsync(
        ClaimCouponRequest request,
        CouponClaimReservationContext reservationContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reservationContext);

        var apiBody = new ClaimCouponApiRequest(
            $"{request.OrderIdempotencyKey}:cpn",
            request.CouponCode,
            request.UserId,
            request.OrderAmount);

        try
        {
            var response = await api.ClaimAsync(apiBody, cancellationToken);
            return new ClaimCouponResponse(response.ClaimId, response.DiscountAmount, response.ExpiresAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ShouldEnqueueInventoryCompensation(ex, cancellationToken))
                await TryEnqueueInventoryReleaseAsync(reservationContext).ConfigureAwait(false);

            switch (ex)
            {
                case ApiException apiEx:
                    throw MapApiException(apiEx);
                case TaskCanceledException tcx when !cancellationToken.IsCancellationRequested:
                    throw new CouponUnavailableException("Coupon claim request timed out.", tcx);
                default:
                    throw new CouponUnavailableException(ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Avoid enqueuing release only on confirmed-success HTTP responses (2xx ApiException paths are rare /
    /// Refit deserialize failures). Unknown exception types compensate by default per place-order orchestration rule.
    /// </summary>
    private static bool ShouldEnqueueInventoryCompensation(Exception ex, CancellationToken cancellationToken) =>
        ex switch
        {
            ApiException api => (int)api.StatusCode is < 200 or >= 400,
            TaskCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException => true,
            _ => true
        };

    /// <remarks>Writes use <see cref="CancellationToken.None"/> so enqueue is not dropped if the caller token is canceled mid-flight.</remarks>
    private async Task TryEnqueueInventoryReleaseAsync(CouponClaimReservationContext ctx)
    {
        try
        {
            var payload = new InventoryReleaseRequestedV1
            {
                ReservationId = ctx.ReservationId,
                Reason = InventoryReleaseReason
            };
            await ctx.CompensationOutboxWriter.AddAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to enqueue inventory compensation after coupon claim fault. ReservationId={ReservationId}",
                ctx.ReservationId);
        }
    }

    private static Exception MapApiException(ApiException ex)
    {
        var status = ex.StatusCode;
        var code = (int)status;

        if (status == HttpStatusCode.UnprocessableEntity)
        {
            var (_, detail) = TryReadProblemParts(ex.Content);
            return new CouponValidationException(string.IsNullOrWhiteSpace(detail) ? ex.Message : detail!);
        }

        if (status == HttpStatusCode.Conflict)
        {
            var (type, detail) = TryReadProblemParts(ex.Content);
            detail ??= string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
            return new CouponException(
                string.IsNullOrWhiteSpace(type) ? "Coupon.Conflict" : type!,
                detail);
        }

        if (code >= 500)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
            return new CouponUnavailableException(msg, ex);
        }

        if (code is >= 400 and < 500)
        {
            var (_, detail) = TryReadProblemParts(ex.Content);
            return new CouponValidationException(string.IsNullOrWhiteSpace(detail) ? ex.Message : detail!, ex);
        }

        var fallback = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
        return new CouponUnavailableException(fallback, ex);
    }

    private static (string? Type, string? Detail) TryReadProblemParts(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
            var detail = root.TryGetProperty("detail", out var dEl) ? dEl.GetString() : null;
            return (type, detail);
        }
        catch
        {
            return (null, content);
        }
    }
}
