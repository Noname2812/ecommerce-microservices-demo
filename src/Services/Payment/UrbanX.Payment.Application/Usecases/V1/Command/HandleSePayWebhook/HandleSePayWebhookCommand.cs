using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Attributes;
using UrbanX.Payment.Application.Integrations.SePay;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleSePayWebhook;

[AllowAnonymous]
[DistributedLock(
    SePayIntegrationConstants.DistributedLockResourceTemplate,
    WaitTimeoutSeconds = SePayIntegrationConstants.PaymentDistributedLockWaitSeconds,
    ExpirySeconds = SePayIntegrationConstants.PaymentDistributedLockExpirySeconds)]
public sealed record HandleSePayWebhookCommand(
    Guid PaymentId,
    string ExternalTransactionId,
    decimal TransferAmount,
    string TransferType,
    string Content,
    string RawPayloadJson
) : ICommand<SePayWebhookResult>;

public sealed record SePayWebhookResult(bool Success, string? Message = null);

public sealed class HandleSePayWebhookCommandValidator : AbstractValidator<HandleSePayWebhookCommand>
{
    public HandleSePayWebhookCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.ExternalTransactionId).NotEmpty().MaximumLength(SePayIntegrationConstants.ExternalTransactionIdMaxLength);
        RuleFor(x => x.TransferAmount).GreaterThan(0);
        RuleFor(x => x.TransferType).NotEmpty().MaximumLength(SePayIntegrationConstants.TransferTypeMaxLength);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(SePayIntegrationConstants.WebhookContentMaxLength);
        RuleFor(x => x.RawPayloadJson).NotEmpty().MaximumLength(SePayIntegrationConstants.WebhookRawPayloadMaxLength);
    }
}
