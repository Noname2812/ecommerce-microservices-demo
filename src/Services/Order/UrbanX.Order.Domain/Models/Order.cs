using Shared.Kernel.Domain;
using UrbanX.Order.Domain.ValueObjects;
using System.Text.Json;

namespace UrbanX.Order.Domain.Models;

public sealed class Order : BaseEntity<Guid>
{
    public string OrderNumber { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? CouponClaimId { get; private set; }
    public string CustomerEmail { get; private set; } = null!;
    public string CustomerName { get; private set; } = null!;
    public string? CustomerPhone { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal ShippingFee { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal FinalAmount { get; private set; }
    public string PricingSnapshot { get; private set; } = "{}";
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
    public string? PaymentUrl { get; private set; }
    public string? QrCodeUrl { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string OrderType { get; private set; } = Models.OrderType.Normal;
    public Guid? CampaignId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderStatusHistory> _statusHistory = new();
    public IReadOnlyList<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private Order() { }

    public static Order Create(
        string orderNumber,
        Guid userId,
        string customerEmail,
        string customerName,
        string? customerPhone,
        ShippingAddress shippingAddress,
        decimal shippingFee,
        string? couponCode,
        decimal couponDiscount,
        string? customerNote,
        string idempotencyKey,
        IReadOnlyList<NewOrderItemSpec> items,
        string orderType = Models.OrderType.Normal,
        Guid? campaignId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            UserId = userId,
            ReservationId = null,
            CouponClaimId = null,
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
            UpdatedAt = now,
            OrderType = orderType,
            CampaignId = campaignId
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
        var grossBeforeCoupon = order.Subtotal + shippingFee + order.TaxAmount;
        var rawDiscountTotal = couponDiscount + order._items.Sum(i => i.DiscountAmount);
        order.DiscountAmount = Math.Min(rawDiscountTotal, grossBeforeCoupon);
        order.TotalAmount = Math.Max(0, grossBeforeCoupon - couponDiscount);
        order.FinalAmount = order.TotalAmount;
        order.PricingSnapshot = JsonSerializer.Serialize(new
        {
            order.Subtotal,
            order.DiscountAmount,
            order.ShippingFee,
            order.TaxAmount,
            order.TotalAmount,
            order.FinalAmount,
            CapturedAt = now
        });

        order._statusHistory.Add(OrderStatusHistory.Create(
            orderId, null, OrderStatus.Pending, "Order placed", userId, customerName));

        return order;
    }

    public bool CanBeCancelledBy(Guid userId) =>
        (Status == OrderStatus.Pending || Status == OrderStatus.Confirmed) &&
        UserId == userId;

    public void SetConfirmedWithReservation(Guid reservationId, Guid? claimId, Guid changedById, string changedByName)
    {
        var prev = Status;
        ReservationId = reservationId;
        CouponClaimId = claimId;
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.Confirmed, null, changedById, changedByName));
    }

    public void SetConfirmedAsSalesOrder(
        Guid reservationId, Guid? claimId,
        Guid campaignId,
        Guid changedById, string changedByName)
    {
        var prev = Status;
        ReservationId = reservationId;
        CouponClaimId = claimId;
        CampaignId = campaignId;
        OrderType = Models.OrderType.Sales;
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.Confirmed, null, changedById, changedByName));
    }

    public void SetPaymentSession(string paymentUrl, string? qrCodeUrl)
    {
        PaymentUrl = paymentUrl;
        QrCodeUrl = qrCodeUrl;
        PaymentStatus = Models.PaymentStatus.AwaitingPayment;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaid(string paymentSessionId, Guid changedById, string changedByName)
    {
        PaymentStatus = Models.PaymentStatus.Paid;
        PaymentReference = paymentSessionId;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, Status, Status, "Payment completed", changedById, changedByName));
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

    public void MarkDeleted()
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
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
