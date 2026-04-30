using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.FailPayment;

[RequirePermission(Permissions.Payment.Write)]
public record FailPaymentCommand(
    Guid PaymentId,
    string FailureReason
) : ICommand;

public sealed class FailPaymentCommandValidator : AbstractValidator<FailPaymentCommand>
{
    public FailPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.FailureReason).NotEmpty().MaximumLength(500);
    }
}
