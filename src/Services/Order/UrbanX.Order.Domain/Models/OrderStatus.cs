namespace UrbanX.Order.Domain.Models;

public static class OrderStatus
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Processing = "PROCESSING";
    public const string Shipped = "SHIPPED";
    public const string Delivered = "DELIVERED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string RefundRequested = "REFUND_REQUESTED";
    public const string Refunded = "REFUNDED";
}
