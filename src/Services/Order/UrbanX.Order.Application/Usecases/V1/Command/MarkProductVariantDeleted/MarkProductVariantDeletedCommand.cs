using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command.MarkProductVariantDeleted;

[AllowAnonymous]
public record MarkProductVariantDeletedCommand(Guid VariantId) : ICommand;

public sealed class MarkProductVariantDeletedCommandValidator
    : AbstractValidator<MarkProductVariantDeletedCommand>
{
    public MarkProductVariantDeletedCommandValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty();
    }
}
