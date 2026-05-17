namespace UrbanX.Order.Domain.Models;

public static class PaymentStatus
{
    public const string Unpaid = "UNPAID";
    public const string AwaitingPayment = "AWAITING_PAYMENT";
    public const string Paid = "PAID";
    public const string Refunded = "REFUNDED";
    public const string PartialRefunded = "PARTIAL_REFUNDED";
}
