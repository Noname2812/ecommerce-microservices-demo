namespace UrbanX.Payment.Domain.ValueObjects;

public static class PaymentStatus
{
    public const string Pending = "PENDING";
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";
}
