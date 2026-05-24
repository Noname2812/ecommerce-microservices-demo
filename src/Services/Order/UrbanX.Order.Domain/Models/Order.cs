using System.Text.Json;
using Shared.Kernel.Domain;
using UrbanX.Order.Domain.Exceptions;
using UrbanX.Order.Domain.ValueObjects;

namespace UrbanX.Order.Domain.Models;

public sealed class Order : BaseEntity<Guid>
{
    public string OrderNumber { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public Guid? CouponClaimId { get; private set; }
    public string CustomerEmail { get; private set; } = null!;
    public string CustomerName { get; private set; } = null!;
    public string? CustomerPhone { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public decimal OriginalPrice { get; private set; }
    public decimal SaleDiscount { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal ShippingFee { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal FinalAmount { get; private set; }
    public string PricingSnapshot { get; private set; } = "{}";
    public string? CouponCode { get; private set; }
    public decimal CouponDiscount { get; private set; }
    public string Status { get; private set; } = OrderStatus.Processing;
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
        Guid orderId,
        string orderNumber,
        Guid userId,
        string customerEmail,
        string customerName,
        string? customerPhone,
        ShippingAddress shippingAddress,
        decimal shippingFee,
        string? couponCode,
        decimal couponDiscount,
        decimal saleDiscount,
        decimal originalPrice,
        string? customerNote,
        string idempotencyKey,
        IReadOnlyList<NewOrderItemSpec> items,
        string orderType = Models.OrderType.Normal,
        Guid? campaignId = null,
        string? paymentMethod = null)
    {
        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            UserId = userId,
            CouponClaimId = null,
            CustomerEmail = customerEmail,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            ShippingAddress = shippingAddress,
            ShippingFee = shippingFee,
            CouponCode = couponCode,
            CouponDiscount = couponDiscount,
            SaleDiscount = saleDiscount,
            OriginalPrice = originalPrice,
            CustomerNote = customerNote,
            IdempotencyKey = idempotencyKey,
            Status = OrderStatus.Processing,
            PaymentStatus = Models.PaymentStatus.Unpaid,
            PaymentMethod = paymentMethod,
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

        order.Subtotal = order._items.Sum(i => i.UnitPrice * i.Quantity);
        var itemLevelDiscounts = order._items.Sum(i => i.DiscountAmount);
        var rawDiscountTotal = saleDiscount + couponDiscount + itemLevelDiscounts;
        order.DiscountAmount = Math.Min(rawDiscountTotal, originalPrice);
        order.FinalAmount = originalPrice - order.DiscountAmount + shippingFee + order.TaxAmount;
        order.TotalAmount = order.FinalAmount;
        order.PricingSnapshot = JsonSerializer.Serialize(new
        {
            order.OriginalPrice,
            order.SaleDiscount,
            order.Subtotal,
            order.DiscountAmount,
            order.ShippingFee,
            order.TaxAmount,
            order.TotalAmount,
            order.FinalAmount,
            CapturedAt = now
        });

        order._statusHistory.Add(OrderStatusHistory.Create(
            orderId, null, OrderStatus.Processing, "Order created", userId, customerName));

        return order;
    }

    public bool CanBeCancelledBy(Guid userId) =>
        (Status == OrderStatus.Processing
         || Status == OrderStatus.PendingPayment
         || Status == OrderStatus.Confirmed)
        && UserId == userId;

    /// <summary>
    /// Applies the coupon resolved from a Cart-time hold token. Recomputes <see cref="DiscountAmount"/>,
    /// <see cref="FinalAmount"/>, <see cref="TotalAmount"/>, and <see cref="PricingSnapshot"/>.
    /// Caller-only: the place-order saga, once after resolving the hold but before payment.
    /// </summary>
    public void ApplyCoupon(string couponCode, decimal couponDiscount)
    {
        if (Status != OrderStatus.Processing) return;

        CouponCode = couponCode;
        CouponDiscount = couponDiscount;

        var itemLevelDiscounts = _items.Sum(i => i.DiscountAmount);
        var rawDiscountTotal = SaleDiscount + couponDiscount + itemLevelDiscounts;
        DiscountAmount = Math.Min(rawDiscountTotal, OriginalPrice);
        FinalAmount = OriginalPrice - DiscountAmount + ShippingFee + TaxAmount;
        TotalAmount = FinalAmount;
        UpdatedAt = DateTimeOffset.UtcNow;

        PricingSnapshot = JsonSerializer.Serialize(new
        {
            OriginalPrice,
            SaleDiscount,
            CouponDiscount,
            Subtotal,
            DiscountAmount,
            ShippingFee,
            TaxAmount,
            TotalAmount,
            FinalAmount,
            CapturedAt = UpdatedAt
        });
    }

    public void MarkReadyForPayment(
        Guid? claimId,
        string paymentUrl, string? qrCodeUrl,
        Guid changedById, string changedByName)
    {
        if (Status != OrderStatus.Processing) return;

        var prev = Status;
        CouponClaimId = claimId;
        PaymentUrl = paymentUrl;
        QrCodeUrl = qrCodeUrl;
        PaymentStatus = Models.PaymentStatus.AwaitingPayment;
        Status = OrderStatus.PendingPayment;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.PendingPayment, "Awaiting payment", changedById, changedByName));
    }

    public void MarkPaid(string paymentSessionId, Guid changedById, string changedByName)
    {
        if (Status == OrderStatus.Cancelled) return;
        if (Status == OrderStatus.Confirmed && PaymentStatus == Models.PaymentStatus.Paid) return;
        if (Status != OrderStatus.PendingPayment)
            throw new OrderExceptions.CannotMarkPaidInStatus(Status);

        var prev = Status;
        Status = OrderStatus.Confirmed;
        PaymentStatus = Models.PaymentStatus.Paid;
        PaymentReference = paymentSessionId;
        UpdatedAt = DateTimeOffset.UtcNow;
        _statusHistory.Add(OrderStatusHistory.Create(
            Id, prev, OrderStatus.Confirmed, "Payment completed", changedById, changedByName));
    }

    public void Cancel(string reason, Guid? changedById, string? changedByName)
    {
        if (Status == OrderStatus.Cancelled) return;

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
