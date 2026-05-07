using Refit;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>Refit contract for Promotion internal coupon claim API.</summary>
public interface ICouponApi
{
    [Post("/internal/v1/coupon-claims")]
    Task<ClaimCouponApiResponse> ClaimAsync([Body] ClaimCouponApiRequest body, CancellationToken cancellationToken);
}

public sealed record ClaimCouponApiRequest(
    string IdempotencyKey,
    string CouponCode,
    Guid UserId,
    decimal OrderAmount);

/// <summary>JSON body aligned with <see cref="UrbanX.Promotion.Application.Usecases.V1.Command.ClaimCouponResult"/>.</summary>
public sealed record ClaimCouponApiResponse(Guid ClaimId, decimal DiscountAmount, DateTimeOffset ExpiresAt);
