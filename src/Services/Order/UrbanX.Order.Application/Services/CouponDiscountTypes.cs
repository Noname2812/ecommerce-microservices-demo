namespace UrbanX.Order.Application.Services;

/// <summary>
/// Wire-format values for <c>CouponLockInfo.DiscountType</c> /
/// <c>SaleEligibility.DiscountType</c>. Promotion-side contract uses the same string literals,
/// so keep these constants stable. Use the <see cref="IsValid"/> helper at the adapter boundary
/// to reject unexpected values rather than scattering equality checks across the codebase.
/// </summary>
public static class CouponDiscountTypes
{
    public const string Fixed   = "FIXED";
    public const string Percent = "PERCENT";

    public static bool IsValid(string? value) =>
        value is Fixed or Percent;
}
