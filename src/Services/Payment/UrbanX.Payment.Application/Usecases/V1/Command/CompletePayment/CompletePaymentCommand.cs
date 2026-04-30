using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CompletePayment;

[RequirePermission(Permissions.Payment.Write)]
public record CompletePaymentCommand(
    Guid PaymentId,
    string? ProviderTransactionId = null
) : ICommand;

public sealed class CompletePaymentCommandValidator : AbstractValidator<CompletePaymentCommand>
{
    public CompletePaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
