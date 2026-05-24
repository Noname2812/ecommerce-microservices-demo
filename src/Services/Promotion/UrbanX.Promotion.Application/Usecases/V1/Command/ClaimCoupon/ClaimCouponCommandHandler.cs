using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Errors;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class ClaimCouponCommandHandler(
    ICouponRepository couponRepository,
    ICouponClaimRepository couponClaimRepository,
    ICouponClaimRedisGateway couponClaimRedis)
    : ICommandHandler<ClaimCouponCommand, ClaimCouponResult>
{
    private static readonly TimeSpan ClaimHoldTtl = TimeSpan.FromMinutes(15);

    public async Task<Result<ClaimCouponResult>> Handle(ClaimCouponCommand request, CancellationToken cancellationToken)
    {
        // IdempotencyPipelineBehavior already short-circuits on cache hit (Redis, 24h TTL).
        // This DB check is the second safety net for cache miss (eviction, Redis restart).
        var existing = await couponClaimRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return Result.Success(MapFromClaim(existing));

        var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
        var coupon = await couponRepository.GetByCodeAsync(normalizedCode, cancellationToken);
        if (coupon is null)
            return Result.Failure<ClaimCouponResult>(CouponErrors.NotFound);

        var now = DateTimeOffset.UtcNow;
        var validationError = ValidateCouponWindow(coupon, request.OrderAmount, now);
        if (validationError is not null)
            return Result.Failure<ClaimCouponResult>(validationError);

        // Phase 3 hold-token path: Cart already acquired user-lock + quota slot at hold time.
        // Re-acquiring here would always fail (SET NX). Skip the Redis steps; trust the hold contract.
        if (request.HoldToken is null)
        {
            if (!await couponClaimRedis.TryAcquireUserHoldAsync(coupon.Id, request.UserId, ClaimHoldTtl, cancellationToken))
                return Result.Failure<ClaimCouponResult>(CouponErrors.AlreadyUsed);

            if (coupon.TotalQuota.HasValue)
            {
                var initialRemaining = coupon.TotalQuota.Value - coupon.UsedQuota;
                var safeInitial = initialRemaining < 0 ? 0 : initialRemaining;

                if (!await couponClaimRedis.TryConsumeQuotaSlotAsync(coupon.Id, request.UserId, safeInitial, cancellationToken))
                    return Result.Failure<ClaimCouponResult>(CouponErrors.Exhausted);
            }
        }

        var discountAmount = CalculateDiscount(coupon, request.OrderAmount);
        var expiresAt = now.Add(ClaimHoldTtl);

        var claim = new CouponClaim
        {
            Id = Guid.NewGuid(),
            CouponCode = coupon.Id,
            UserId = request.UserId,
            OrderIdempotencyKey = request.IdempotencyKey,
            DiscountAmount = discountAmount,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            RestoreQuotaSlotOnRelease = coupon.TotalQuota.HasValue
        };

        await couponClaimRepository.AddAsync(claim, cancellationToken);

        coupon.UsedQuota++;
        await couponRepository.UpdateAsync(coupon, cancellationToken);

        return Result.Success(new ClaimCouponResult(claim.Id, discountAmount, expiresAt));
    }

    private static ClaimCouponResult MapFromClaim(CouponClaim claim) =>
        new(claim.Id, claim.DiscountAmount, claim.ExpiresAt);

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
