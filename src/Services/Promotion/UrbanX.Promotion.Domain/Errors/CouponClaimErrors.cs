using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Domain.Errors;

/// <summary>Error codes for internal coupon claim lifecycle (DELETE release).</summary>
public static class CouponClaimErrors
{
    public static Error NotFound(Guid claimId) =>
        new("CouponClaim.NotFound", $"Coupon claim {claimId} was not found");

    public static Error InvalidStatusForRelease(string currentStatus) =>
        new(
            "CouponClaim.InvalidStatus",
            $"Coupon claim cannot be released from status \"{currentStatus}\"");
}
