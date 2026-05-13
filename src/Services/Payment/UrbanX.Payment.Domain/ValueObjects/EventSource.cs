namespace UrbanX.Payment.Domain.ValueObjects;

public static class EventSource
{
    public const string Internal = "INTERNAL";
    public const string WebhookStripe = "WEBHOOK_STRIPE";
    public const string WebhookMomo = "WEBHOOK_MOMO";
    public const string WebhookVnpay = "WEBHOOK_VNPAY";
    public const string WebhookSepay = "WEBHOOK_SEPAY";
}
