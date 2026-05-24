using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

/// <summary>
/// Cart-time coupon hold. Issues a short-lived token the client carries to checkout.
/// Redis-only: no DB write. Discount metadata is read from DB (single SELECT) but the hold itself
/// (user lock, quota slot, token) lives entirely in Redis.
/// </summary>
[AllowAnonymous]
public record HoldCouponCommand(
    string CouponCode,
    Guid UserId,
    decimal OrderAmount
) : ICommand<HoldCouponResult>;

public record HoldCouponResult(
    string HoldToken,
    decimal DiscountAmount,
    string DiscountType,
    DateTimeOffset ExpiresAt);

public sealed class HoldCouponCommandValidator : AbstractValidator<HoldCouponCommand>
{
    public HoldCouponCommandValidator()
    {
        RuleFor(x => x.CouponCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OrderAmount).GreaterThan(0);
    }
}
