using Shared.Kernel.Primitives;

namespace UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

/// <summary>
/// Thrown when <see cref="Usecases.V1.Command.ReleaseCouponClaimCommand"/> returns failure inside the coupon compensation pipeline.
/// Treated as transient by <see cref="CouponReleaseRequestedConsumer"/> so <c>UseMessageRetry</c> can reattempt without fatal noise each time.
/// </summary>
internal sealed class CouponReleaseCommandFailedException(Error error)
    : Exception($"Coupon release failed: {error.Code}")
{
    public string ErrorCode { get; } = error.Code;
}
