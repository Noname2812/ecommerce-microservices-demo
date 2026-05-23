using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreatePaymentSession;

[AllowAnonymous]
public sealed record CreatePaymentSessionCommand(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    Guid? CustomerId,
    string? CustomerEmail
) : ICommand<CreatePaymentSessionResult>;

public sealed record CreatePaymentSessionResult(
    Guid PaymentId,
    string QrCodeUrl,
    string BankAccount,
    string BankCode,
    string TransferReference,
    DateTimeOffset ExpiresAt);

public sealed class CreatePaymentSessionCommandValidator : AbstractValidator<CreatePaymentSessionCommand>
{
    public CreatePaymentSessionCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(10);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(255);
    }
}
