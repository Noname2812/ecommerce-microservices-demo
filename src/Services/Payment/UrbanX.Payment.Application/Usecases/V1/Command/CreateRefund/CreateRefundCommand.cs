using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreateRefund;

[AllowAnonymous]
public record CreateRefundCommand(
    Guid OrderId,
    decimal Amount,
    string? Reason = null
) : ICommand<Guid>;

public sealed class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(255).When(x => x.Reason is not null);
    }
}
