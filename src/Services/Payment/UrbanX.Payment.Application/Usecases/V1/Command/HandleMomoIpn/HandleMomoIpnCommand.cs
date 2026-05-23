using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Payment.Application.Integrations.Momo;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleMomoIpn;

[AllowAnonymous]
public sealed record HandleMomoIpnCommand(
    string PartnerCode,
    string OrderId,
    string RequestId,
    decimal Amount,
    long TransId,
    int ResultCode,
    string Message,
    string OrderType,
    string PayType,
    long ResponseTime,
    string ExtraData,
    string Signature,
    string RawPayloadJson
) : ICommand<MomoIpnResult>;

public sealed record MomoIpnResult(bool Success, string? Message = null);

public sealed class HandleMomoIpnCommandValidator : AbstractValidator<HandleMomoIpnCommand>
{
    public HandleMomoIpnCommandValidator()
    {
        RuleFor(x => x.PartnerCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OrderId).NotEmpty().MaximumLength(MomoIntegrationConstants.OrderIdMaxLength);
        RuleFor(x => x.RequestId).NotEmpty().MaximumLength(MomoIntegrationConstants.RequestIdMaxLength);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TransId).GreaterThan(0);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(256);
        RuleFor(x => x.RawPayloadJson).NotEmpty().MaximumLength(MomoIntegrationConstants.WebhookRawPayloadMaxLength);
    }
}
