using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command.UpdateProductStatusProjection;

[AllowAnonymous]
public record UpdateProductStatusProjectionCommand(Guid ProductId, bool IsActive) : ICommand;

public sealed class UpdateProductStatusProjectionCommandValidator
    : AbstractValidator<UpdateProductStatusProjectionCommand>
{
    public UpdateProductStatusProjectionCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
