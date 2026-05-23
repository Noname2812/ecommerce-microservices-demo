namespace UrbanX.Payment.Application.Integrations.Momo;

public static class MomoIntegrationConstants
{
    public const string WebhookPathSegment = "/api/v1/payments/webhook/momo";

    // Distributed lock template for IPN command (payment:<PaymentId>)
    public const string DistributedLockResourceTemplate = "payment:{PaymentId}";
    public const int PaymentDistributedLockWaitSeconds = 5;
    public const int PaymentDistributedLockExpirySeconds = 30;

    public const int ExternalTransactionIdMaxLength = 64;
    public const int OrderIdMaxLength = 64;
    public const int RequestIdMaxLength = 64;
    public const int WebhookRawPayloadMaxLength = 8192;

    // MoMo resultCodes — treat as success
    public const int ResultCodeSuccess = 0;
    public const int ResultCodeAuthorized = 9000;

    // MoMo resultCodes — treat as pending (do not finalize)
    public const int ResultCodeInitiated = 1000;
    public const int ResultCodeProcessing = 7000;
    public const int ResultCodeProcessingPay = 7002;

    // MoMo orderId prefix used when creating session (so IPN can lookup by transfer_reference)
    public const string OrderIdPrefix = "UX-";

    // Refund description max length per MoMo spec
    public const int RefundDescriptionMaxLength = 200;
}
