using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class HoldCouponCommandHandler(
    ICouponRepository couponRepository,
    ICouponClaimRedisGateway claimRedis,
    ICouponHoldGateway holdGateway)
    : ICommandHandler<HoldCouponCommand, HoldCouponResult>
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(15);

    public async Task<Result<HoldCouponResult>> Handle(HoldCouponCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
        var coupon = await couponRepository.GetByCodeAsync(normalizedCode, cancellationToken);
        if (coupon is null)
            return Result.Failure<HoldCouponResult>(CouponErrors.NotFound);

        var now = DateTimeOffset.UtcNow;
        var validationError = ValidateCouponWindow(coupon, request.OrderAmount, now);
        if (validationError is not null)
            return Result.Failure<HoldCouponResult>(validationError);

        if (!await claimRedis.TryAcquireUserHoldAsync(coupon.Id, request.UserId, HoldTtl, cancellationToken))
            return Result.Failure<HoldCouponResult>(CouponErrors.AlreadyUsed);

        if (coupon.TotalQuota.HasValue)
        {
            var initialRemaining = coupon.TotalQuota.Value - coupon.UsedQuota;
            var safeInitial = initialRemaining < 0 ? 0 : initialRemaining;

            if (!await claimRedis.TryConsumeQuotaSlotAsync(coupon.Id, request.UserId, safeInitial, cancellationToken))
                return Result.Failure<HoldCouponResult>(CouponErrors.Exhausted);
        }

        var discountAmount = CalculateDiscount(coupon, request.OrderAmount);
        var expiresAt = now.Add(HoldTtl);
        var token = Guid.NewGuid().ToString("N");

        var info = new CouponHoldInfo(
            CouponCode: coupon.Id,
            UserId: request.UserId,
            DiscountAmount: discountAmount,
            DiscountType: coupon.DiscountType,
            OrderAmount: request.OrderAmount,
            ExpiresAt: expiresAt);

        await holdGateway.SetHoldAsync(token, info, HoldTtl, cancellationToken);

        return Result.Success(new HoldCouponResult(token, discountAmount, coupon.DiscountType, expiresAt));
    }

    private static Error? ValidateCouponWindow(Coupon coupon, decimal orderAmount, DateTimeOffset now)
    {
        if (!coupon.IsActive)
            return CouponErrors.Inactive;

        if (now < coupon.ValidFrom || now > coupon.ExpiresAt)
            return CouponErrors.Expired;

        if (orderAmount < coupon.MinOrderValue)
            return CouponErrors.OrderBelowMinValue(coupon.MinOrderValue, orderAmount);

        return null;
    }

    private static decimal CalculateDiscount(Coupon coupon, decimal orderAmount) =>
        coupon.DiscountType switch
        {
            var t when t == DiscountType.FixedAmount => Math.Min(coupon.DiscountValue, orderAmount),
            var t when t == DiscountType.Percentage =>
                Math.Min(
                    orderAmount,
                    Math.Round(orderAmount * coupon.DiscountValue / 100m, 2)),
            _ => 0
        };
}
