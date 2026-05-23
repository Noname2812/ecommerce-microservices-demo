using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Attributes;
using UrbanX.Payment.Application.Integrations.SePay;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CancelPayment;

[RequirePermission(Permissions.Payment.Write)]
[DistributedLock(
    SePayIntegrationConstants.DistributedLockResourceTemplate,
    WaitTimeoutSeconds = SePayIntegrationConstants.PaymentDistributedLockWaitSeconds,
    ExpirySeconds = SePayIntegrationConstants.PaymentDistributedLockExpirySeconds)]
public record CancelPaymentCommand(Guid PaymentId) : ICommand;

public sealed class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
