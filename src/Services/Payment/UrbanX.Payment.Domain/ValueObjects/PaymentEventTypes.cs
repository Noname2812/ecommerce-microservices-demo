namespace UrbanX.Payment.Domain.ValueObjects;

public static class PaymentEventTypes
{
    public const string WebhookPartialReceived = "WEBHOOK_PARTIAL_RECEIVED";
    public const string WebhookReceived = "WEBHOOK_RECEIVED";
    public const string WebhookOverpayment = "WEBHOOK_OVERPAYMENT";
    public const string WebhookReceivedAfterExpiry = "WEBHOOK_RECEIVED_AFTER_EXPIRY";
    public const string PaymentExpired = "PAYMENT_EXPIRED";
}
