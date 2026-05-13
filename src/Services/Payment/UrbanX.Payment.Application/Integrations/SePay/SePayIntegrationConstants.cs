using UrbanX.Payment.Domain;

namespace UrbanX.Payment.Application.Integrations.SePay;

/// <summary>Compile-time and protocol constants for SePay webhooks and distributed locks (must stay const for attributes).</summary>
public static class SePayIntegrationConstants
{
    /// <summary>SePay <c>transferType</c> for incoming bank credits.</summary>
    public const string TransferTypeInbound = "in";

    /// <summary>Redis lock key template; placeholder <c>{PaymentId}</c> for DistributedLockAttribute.</summary>
    public const string DistributedLockResourceTemplate = "payment:{PaymentId}";

    public const int PaymentDistributedLockWaitSeconds = 15;

    public const int PaymentDistributedLockExpirySeconds = 30;

    public const int ExternalTransactionIdMaxLength = PaymentEventConstraints.ExternalTransactionIdMaxLength;

    public const int TransferTypeMaxLength = 20;

    public const int WebhookContentMaxLength = 4000;

    public const int WebhookRawPayloadMaxLength = 100_000;

    /// <summary>Leading/trailing word boundary for matching <c>OrderNumber</c> inside transfer memo.</summary>
    public const string OrderNumberRegexWordBoundary = @"\b";
}
