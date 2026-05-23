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

    /// <summary>Header carrying HMAC-SHA256 signature of the webhook body. Format: <c>sha256=&lt;hex&gt;</c>.</summary>
    public const string HmacHeaderName = "X-SePay-Signature";

    /// <summary>Header carrying the unix-seconds timestamp used in the signed payload.</summary>
    public const string TimestampHeaderName = "X-SePay-Timestamp";

    /// <summary>Prefix expected on the signature header value.</summary>
    public const string HmacHeaderPrefix = "sha256=";

    /// <summary>Path segment under which all SePay webhook routes live (used to gate EnableBuffering).</summary>
    public const string WebhookPathSegment = "/api/v1/payments/webhook";
}
