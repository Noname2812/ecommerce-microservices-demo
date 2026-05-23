using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Integrations.Momo;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo;

internal sealed class MomoPaymentProvider(
    IPaymentProviderRepository paymentProviderRepository,
    IMomoClient momoClient,
    IOptionsSnapshot<MomoOptions> momoOptions,
    ILogger<MomoPaymentProvider> logger) : IPaymentSessionProvider
{
    private const string MomoProviderName = "MoMo";
    private const string RequestType = "captureWallet";
    private const string PartnerName = "UrbanX";
    private const string StoreId = "UrbanXStore";
    private const string OrderInfoPrefix = "UrbanX order ";

    public string Method => ProviderType.Momo;

    public async Task<Result<PaymentSessionArtifact>> CreateSessionAsync(
        PaymentSessionContext context, CancellationToken cancellationToken)
    {
        var provider = await paymentProviderRepository.GetActiveByTypeAsync(ProviderType.Momo, cancellationToken);
        if (provider is null)
        {
            logger.LogWarning("Active MoMo PaymentProvider not found (type={ProviderType}).", ProviderType.Momo);
            return Result.Failure<PaymentSessionArtifact>(PaymentErrors.ProviderNotFound);
        }

        var opts = momoOptions.Value;
        var orderId = $"{MomoIntegrationConstants.OrderIdPrefix}{context.PaymentId:N}";
        var requestId = Guid.NewGuid().ToString("N");
        var amount = (long)Math.Round(context.Amount, 0);
        var orderInfo = OrderInfoPrefix + context.OrderNumber;
        var extraData = string.Empty;

        var signatureFields = new Dictionary<string, string?>
        {
            ["accessKey"]    = opts.AccessKey,
            ["amount"]       = amount.ToString(CultureInfo.InvariantCulture),
            ["extraData"]    = extraData,
            ["ipnUrl"]       = opts.IpnUrl,
            ["orderId"]      = orderId,
            ["orderInfo"]    = orderInfo,
            ["partnerCode"]  = opts.PartnerCode,
            ["redirectUrl"]  = opts.RedirectUrl,
            ["requestId"]    = requestId,
            ["requestType"]  = RequestType
        };

        var signature = MomoSignature.Compute(signatureFields, opts.SecretKey);

        var request = new MomoCreateRequest(
            PartnerCode: opts.PartnerCode,
            PartnerName: PartnerName,
            StoreId: StoreId,
            RequestId: requestId,
            Amount: amount,
            OrderId: orderId,
            OrderInfo: orderInfo,
            RedirectUrl: opts.RedirectUrl,
            IpnUrl: opts.IpnUrl,
            Lang: opts.Lang,
            ExtraData: extraData,
            RequestType: RequestType,
            Signature: signature);

        MomoCreateResponse response;
        try
        {
            response = await momoClient.CreateSessionAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MoMo /create call failed for orderId {OrderId}.", orderId);
            return Result.Failure<PaymentSessionArtifact>(PaymentErrors.MomoGatewayFailed);
        }

        if (response.ResultCode != MomoIntegrationConstants.ResultCodeSuccess)
        {
            logger.LogWarning(
                "MoMo /create rejected orderId {OrderId}: resultCode={ResultCode} message={Message}",
                orderId, response.ResultCode, response.Message);
            return Result.Failure<PaymentSessionArtifact>(PaymentErrors.MomoGatewayFailed);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(opts.RequestExpireSeconds);

        return Result.Success(new PaymentSessionArtifact(
            ProviderName: MomoProviderName,
            ProviderId: provider.Id,
            QrCodeUrl: response.QrCodeUrl,
            BankAccount: null,
            BankCode: null,
            TransferReference: orderId,
            PayUrl: response.PayUrl,
            Deeplink: response.Deeplink,
            ProviderRequestId: requestId,
            ExpiresAt: expiresAt));
    }
}
