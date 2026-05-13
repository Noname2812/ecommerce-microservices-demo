namespace UrbanX.Order.Infrastructure.RefitApi.Coupon.Dtos;

public sealed record ClaimCouponApiRequest(
    string IdempotencyKey,
    string CouponCode,
    Guid UserId,
    decimal OrderAmount
);

public sealed record ClaimCouponApiResponse(
    Guid ClaimId,
    decimal DiscountAmount,
    DateTimeOffset ExpiresAt
);
