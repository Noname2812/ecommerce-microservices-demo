using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[AllowAnonymous]
public record ClaimCouponCommand(
    string IdempotencyKey,
    string CouponCode,
    Guid UserId,
    decimal OrderAmount
) : ICommand<ClaimCouponResult>;

public record ClaimCouponResult(Guid ClaimId, decimal DiscountAmount, DateTimeOffset ExpiresAt);

public sealed class ClaimCouponCommandValidator : AbstractValidator<ClaimCouponCommand>
{
    public ClaimCouponCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.CouponCode).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OrderAmount).GreaterThan(0);
    }
}
