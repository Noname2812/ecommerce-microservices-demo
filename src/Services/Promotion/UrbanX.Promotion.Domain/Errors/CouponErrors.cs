using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Domain.Errors;

/// <summary>
/// Error codes for POST /internal/v1/coupon-claims (internal contract).
/// Uses SCREAMING_SNAKE_CASE codes per place-order spec — differs from the usual "Entity.Problem" style elsewhere.
/// </summary>
public static class CouponErrors
{
    public static readonly Error NotFound =
        new("COUPON_NOT_FOUND", "Coupon does not exist");

    public static readonly Error Inactive =
        new("COUPON_INACTIVE", "Coupon is not active");

    public static readonly Error Expired =
        new("COUPON_EXPIRED", "Coupon is outside its valid period");

    public const string OrderBelowMinValueCode = "ORDER_BELOW_MIN_VALUE";

    public static Error OrderBelowMinValue(decimal minOrderValue, decimal orderAmount) =>
        new(OrderBelowMinValueCode,
            $"Order amount {orderAmount:F2} is below minimum {minOrderValue:F2} for this coupon");

    public static readonly Error AlreadyUsed =
        new("COUPON_ALREADY_USED", "This user has already claimed this coupon");

    public static readonly Error Exhausted =
        new("COUPON_EXHAUSTED", "Coupon usage quota has been exhausted");

    public static bool MapsToHttp422(string code) =>
        code == NotFound.Code
        || code == Inactive.Code
        || code == Expired.Code
        || code == OrderBelowMinValueCode;

    public static bool MapsToHttp409(string code) =>
        code == AlreadyUsed.Code || code == Exhausted.Code;
}
