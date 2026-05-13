using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Attributes;
using UrbanX.Payment.Application.Integrations.SePay;

namespace UrbanX.Payment.Application.Usecases.V1.Command.ExpirePayment;

[AllowAnonymous]
[DistributedLock(
    SePayIntegrationConstants.DistributedLockResourceTemplate,
    WaitTimeoutSeconds = SePayIntegrationConstants.PaymentDistributedLockWaitSeconds,
    ExpirySeconds = SePayIntegrationConstants.PaymentDistributedLockExpirySeconds)]
public sealed record ExpirePaymentCommand(Guid PaymentId) : ICommand;

public sealed class ExpirePaymentCommandValidator : AbstractValidator<ExpirePaymentCommand>
{
    public ExpirePaymentCommandValidator() => RuleFor(x => x.PaymentId).NotEmpty();
}
