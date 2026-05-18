namespace UrbanX.Order.Domain.Models;

public static class OrderStatus
{
    public const string Processing = "PROCESSING";
    public const string PendingPayment = "PENDING_PAYMENT";
    public const string Confirmed = "CONFIRMED";
    public const string Cancelled = "CANCELLED";

    // Future logistics (not in scope for TASK-02)
    public const string Shipped = "SHIPPED";
    public const string Delivered = "DELIVERED";
    public const string RefundRequested = "REFUND_REQUESTED";
    public const string Refunded = "REFUNDED";

    // Legacy — used by CancelOrder until TASK-06+
    public const string Completed = "COMPLETED";
}
