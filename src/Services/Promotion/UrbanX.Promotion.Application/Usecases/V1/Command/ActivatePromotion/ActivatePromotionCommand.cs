using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record ActivatePromotionCommand(Guid Id) : ICommand;

public sealed class ActivatePromotionCommandValidator : AbstractValidator<ActivatePromotionCommand>
{
    public ActivatePromotionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
