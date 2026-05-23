using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Integrations.Momo;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo;

internal sealed class MomoRefundProvider(
    IMomoClient momoClient,
    IOptionsSnapshot<MomoOptions> momoOptions,
    ILogger<MomoRefundProvider> logger) : IPaymentRefundProvider
{
    public string Method => ProviderType.Momo;

    public async Task<Result<string>> RefundAsync(
        Guid refundId,
        Guid paymentId,
        string providerTransactionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(providerTransactionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transId))
        {
            logger.LogWarning(
                "MoMo refund called with non-numeric transId {TransId} for payment {PaymentId}.",
                providerTransactionId, paymentId);
            return Result.Failure<string>(PaymentErrors.RefundFailed);
        }

        var opts = momoOptions.Value;
        // Deterministic ids → MoMo dedups identical refund attempts (idempotent on retry).
        var orderId = $"refund-{refundId:N}";
        var requestId = $"req-{refundId:N}";
        var refundAmount = (long)Math.Round(amount, 0);
        var description = Truncate(reason, MomoIntegrationConstants.RefundDescriptionMaxLength);

        var signatureFields = new Dictionary<string, string?>
        {
            ["accessKey"]   = opts.AccessKey,
            ["amount"]      = refundAmount.ToString(CultureInfo.InvariantCulture),
            ["description"] = description,
            ["orderId"]     = orderId,
            ["partnerCode"] = opts.PartnerCode,
            ["requestId"]   = requestId,
            ["transId"]     = transId.ToString(CultureInfo.InvariantCulture)
        };

        var signature = MomoSignature.Compute(signatureFields, opts.SecretKey);

        var request = new MomoRefundRequest(
            PartnerCode: opts.PartnerCode,
            OrderId: orderId,
            RequestId: requestId,
            Amount: refundAmount,
            TransId: transId,
            Lang: opts.Lang,
            Description: description,
            Signature: signature);

        MomoRefundResponse response;
        try
        {
            response = await momoClient.RefundAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MoMo /refund call failed for payment {PaymentId}.", paymentId);
            return Result.Failure<string>(PaymentErrors.RefundFailed);
        }

        if (response.ResultCode != MomoIntegrationConstants.ResultCodeSuccess)
        {
            logger.LogWarning(
                "MoMo /refund rejected payment {PaymentId}: resultCode={ResultCode} message={Message}",
                paymentId, response.ResultCode, response.Message);
            return Result.Failure<string>(PaymentErrors.RefundFailed);
        }

        // Use MoMo transId of refund as provider refund id
        return Result.Success(response.TransId.ToString(CultureInfo.InvariantCulture));
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= maxLength ? value : value[..maxLength];
}
