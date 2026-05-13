using Refit;
using UrbanX.Order.Infrastructure.RefitApi.Coupon.Dtos;

namespace UrbanX.Order.Infrastructure.RefitApi.Coupon;

/// <summary>Refit contract for Promotion internal coupon claim API.</summary>
public interface ICouponApi
{
    [Post("/internal/v1/coupon-claims")]
    Task<ClaimCouponApiResponse> ClaimAsync(
        [Body] ClaimCouponApiRequest body,
        CancellationToken cancellationToken);
}
