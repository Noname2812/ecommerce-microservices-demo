using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Dtos.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreatePaymentSession;

[AllowAnonymous]
public sealed record CreatePaymentSessionCommand(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    Guid? CustomerId,
    string? CustomerEmail,
    PaymentMethod PaymentMethod
) : ICommand<CreatePaymentSessionResult>;

public sealed record CreatePaymentSessionResult(
    Guid PaymentId,
    string ProviderName,
    string? QrCodeUrl,
    string? BankAccount,
    string? BankCode,
    string? TransferReference,
    string? PayUrl,
    string? Deeplink,
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
        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("PaymentMethod must be a known value ('Sepay' or 'Momo').");
    }
}
