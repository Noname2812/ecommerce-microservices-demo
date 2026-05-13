using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Payment.Application.Integrations.SePay;

namespace UrbanX.Payment.Application.Usecases.V1.Query.ResolveSePayWebhookPayment;

[AllowAnonymous]
public sealed record ResolveSePayWebhookPaymentQuery(string Content) : IQuery<Guid?>;

public sealed class ResolveSePayWebhookPaymentQueryValidator : AbstractValidator<ResolveSePayWebhookPaymentQuery>
{
    public ResolveSePayWebhookPaymentQueryValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(SePayIntegrationConstants.WebhookContentMaxLength);
    }
}
