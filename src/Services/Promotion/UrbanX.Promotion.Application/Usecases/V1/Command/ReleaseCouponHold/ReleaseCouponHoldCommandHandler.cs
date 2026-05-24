using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Abstractions;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class ReleaseCouponHoldCommandHandler(
    ICouponHoldGateway holdGateway,
    ICouponClaimRedisGateway claimRedis)
    : ICommandHandler<ReleaseCouponHoldCommand>
{
    public async Task<Result> Handle(ReleaseCouponHoldCommand request, CancellationToken cancellationToken)
    {
        var info = await holdGateway.TryGetAsync(request.HoldToken, cancellationToken);
        if (info is null)
            return Result.Success();

        // Restore quota slot only when the coupon had a quota in the first place (mirrors claim-time logic).
        // Best-effort: keep going even if the per-user lock or quota key is missing — TTL will mop up.
        await claimRedis.ReleaseClaimRedisStateAsync(
            couponCode: info.CouponCode,
            userId: info.UserId,
            incrementQuotaRemaining: true,
            ct: cancellationToken);

        await holdGateway.TryDeleteAsync(request.HoldToken, cancellationToken);

        return Result.Success();
    }
}
