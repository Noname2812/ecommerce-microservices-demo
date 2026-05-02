using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record PausePromotionCommand(Guid Id) : ICommand;

public sealed class PausePromotionCommandValidator : AbstractValidator<PausePromotionCommand>
{
    public PausePromotionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
