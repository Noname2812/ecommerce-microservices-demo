using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Infrastructure.Integrations.SePay;

internal sealed class SePayPaymentProvider(
    IPaymentProviderRepository paymentProviderRepository,
    IOptionsSnapshot<SePayOptions> sePayOptions,
    ILogger<SePayPaymentProvider> logger) : IPaymentSessionProvider
{
    private const string QrEndpoint = "https://qr.sepay.vn/img";
    private const string SePayProviderName = "SePay";

    public string Method => ProviderType.Sepay;

    public async Task<Result<PaymentSessionArtifact>> CreateSessionAsync(
        PaymentSessionContext context, CancellationToken cancellationToken)
    {
        var provider = await paymentProviderRepository.GetActiveByTypeAsync(ProviderType.Sepay, cancellationToken);
        if (provider is null)
        {
            logger.LogWarning("Active SePay PaymentProvider not found (type={ProviderType}).", ProviderType.Sepay);
            return Result.Failure<PaymentSessionArtifact>(PaymentErrors.ProviderNotFound);
        }

        var opts = sePayOptions.Value;
        var transferReference = context.OrderNumber;
        var qrUrl = BuildVietQrUrl(opts, context.Amount, transferReference);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(opts.PaymentSessionExpiresAfterMinutes);

        return Result.Success(new PaymentSessionArtifact(
            ProviderName: SePayProviderName,
            ProviderId: provider.Id,
            QrCodeUrl: qrUrl,
            BankAccount: opts.BankAccount,
            BankCode: opts.BankCode,
            TransferReference: transferReference,
            PayUrl: null,
            Deeplink: null,
            ProviderRequestId: null,
            ExpiresAt: expiresAt));
    }

    private static string BuildVietQrUrl(SePayOptions opts, decimal amount, string transferReference)
    {
        var amountStr = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var qs =
            $"acc={Uri.EscapeDataString(opts.BankAccount)}" +
            $"&bank={Uri.EscapeDataString(opts.BankCode)}" +
            $"&amount={Uri.EscapeDataString(amountStr)}" +
            $"&des={Uri.EscapeDataString(transferReference)}" +
            $"&template={Uri.EscapeDataString(opts.QrTemplate)}";
        return $"{QrEndpoint}?{qs}";
    }
}
