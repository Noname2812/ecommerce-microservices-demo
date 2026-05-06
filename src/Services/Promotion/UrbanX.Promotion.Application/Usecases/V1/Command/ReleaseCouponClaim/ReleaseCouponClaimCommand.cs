using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[AllowAnonymous]
public record ReleaseCouponClaimCommand(Guid ClaimId, Guid? EventId = null) : ICommand;

public sealed class ReleaseCouponClaimCommandValidator : AbstractValidator<ReleaseCouponClaimCommand>
{
    public ReleaseCouponClaimCommandValidator()
    {
        RuleFor(x => x.ClaimId).NotEmpty();
    }
}
