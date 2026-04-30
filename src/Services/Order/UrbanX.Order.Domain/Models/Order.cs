using Shared.Kernel.Domain;
using UrbanX.Order.Domain.ValueObjects;

namespace UrbanX.Order.Domain.Models;

public sealed class Order : BaseEntity<Guid>
{
    public string OrderNumber { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = null!;
    public string CustomerName { get; private set; } = null!;
    public string? CustomerPhone { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal ShippingFee { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string? CouponCode { get; private set; }
    public decimal CouponDiscount { get; private set; }
    public string Status { get; private set; } = OrderStatus.Pending;
    public string PaymentStatus { get; private set; } = Models.PaymentStatus.Unpaid;
    public string? PaymentMethod { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? ShippingMethod { get; private set; }
    public string? TrackingNumber { get; private set; }
    public DateTimeOffset? EstimatedDeliveryAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? CustomerNote { get; private set; }
    public string? InternalNote { get; private set; }
    public string? CancelledReason { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderStatusHistory> _statusHistory = new();
    public IReadOnlyList<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private Order() { }

    public static Order Create(
        string orderNumber,
        Guid customerId,
        string customerEmail,
        string customerName,
        string? customerPhone,
        ShippingAddress shippingAddress,
        decimal shippingFee,
        string? couponCode,
        decimal couponDiscount,
        string? customerNote,
        string? idempotencyKey,
        IReadOnlyList<NewOrderItemSpec> items)
    {
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            ShippingAddress = shippingAddress,
            ShippingFee = shippingFee,
            CouponCode = couponCode,
            CouponDiscount = couponDiscount,
            CustomerNote = customerNote,
            IdempotencyKey = idempotencyKey,
            Status = OrderStatus.Pending,
            PaymentStatus = Models.PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var spec in items)
        {
            var item = OrderItem.Create(
                orderId,
                spec.ProductId, spec.ProductName, spec.ProductSlug,
                spec.VariantId, spec.VariantSku, spec.VariantName,
                spec.SellerId, spec.SellerName,
                spec.UnitPrice, spec.Quantity, spec.DiscountAmount, spec.ImageUrl);
            order._items.Add(item);
        }

        order.Subtotal = order._items.Sum(i => i.Subtotal);
        order.DiscountAmount = couponDiscount + order._items.Sum(i => i.DiscountAmount);
        order.TotalAmount = order.Subtotal + shippingFee - couponDiscount;

        order._statusHistory.Add(OrderStatusHistory.Create(
            orderId, null, OrderStatus.Pending, "Order placed", customerId, customerName));

        return order;
    }

    public bool CanBeCancelledBy(Guid userId) =>
        (Status == OrderStatus.Pending || Status == OrderStatus.Confirmed) &&
        CustomerId == userId;

    public void Confirm(Guid changedById, string changedByName)
    {
        var prev = Status;
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.Confirmed, null, changedById, changedByName));
    }

    public void Cancel(string reason, Guid? changedById, string? changedByName)
    {
        var prev = Status;
        Status = OrderStatus.Cancelled;
        CancelledReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.Cancelled, reason, changedById, changedByName));
    }
}

public record NewOrderItemSpec(
    Guid ProductId,
    string ProductName,
    string? ProductSlug,
    Guid VariantId,
    string VariantSku,
    string? VariantName,
    Guid SellerId,
    string SellerName,
    decimal UnitPrice,
    int Quantity,
    decimal DiscountAmount,
    string? ImageUrl
);
