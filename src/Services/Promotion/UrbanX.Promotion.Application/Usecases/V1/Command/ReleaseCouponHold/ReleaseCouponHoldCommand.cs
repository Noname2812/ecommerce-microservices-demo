using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

/// <summary>
/// Releases a Cart-time hold before TTL — e.g. user removed the coupon at Cart.
/// Idempotent: returning <c>Result.Success</c> when the token is already gone.
/// </summary>
[AllowAnonymous]
public record ReleaseCouponHoldCommand(string HoldToken) : ICommand;

public sealed class ReleaseCouponHoldCommandValidator : AbstractValidator<ReleaseCouponHoldCommand>
{
    public ReleaseCouponHoldCommandValidator()
    {
        RuleFor(x => x.HoldToken).NotEmpty().MaximumLength(64);
    }
}
