using UrbanX.Order.Domain.Exceptions;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Services.Order.UnitTests.Domain.Models;

public class OrderTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChangedById = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_WithExternalOrderId_SetsStatusProcessing()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        // Act
        var order = CreateOrder(orderId: orderId);

        // Assert
        Assert.Equal(orderId, order.Id);
        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.Single(order.StatusHistory);
        Assert.Null(order.StatusHistory[0].FromStatus);
        Assert.Equal(OrderStatus.Processing, order.StatusHistory[0].ToStatus);
        Assert.Equal("Order created", order.StatusHistory[0].Note);
    }

    [Fact]
    public void Create_SetsPricingFieldsFromParameters()
    {
        // Arrange
        const decimal originalPrice = 100m;
        const decimal saleDiscount = 20m;
        const decimal couponDiscount = 10m;

        // Act
        var order = CreateOrder(
            originalPrice: originalPrice,
            saleDiscount: saleDiscount,
            couponDiscount: couponDiscount);

        // Assert
        Assert.Equal(originalPrice, order.OriginalPrice);
        Assert.Equal(saleDiscount, order.SaleDiscount);
        Assert.Equal(couponDiscount, order.CouponDiscount);
        Assert.Equal(30m, order.DiscountAmount);
        Assert.Equal(70m, order.FinalAmount);
    }

    [Fact]
    public void MarkReadyForPayment_WhenProcessing_MovesToPendingPayment()
    {
        // Arrange
        var order = CreateOrder();
        var reservationId = Guid.NewGuid();

        // Act
        order.MarkReadyForPayment(
            reservationId, null,
            "https://pay.example/checkout",
            "https://pay.example/qr",
            ChangedById, "System");

        // Assert
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(reservationId, order.ReservationId);
        Assert.Equal("https://pay.example/checkout", order.PaymentUrl);
        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(2, order.StatusHistory.Count);
        Assert.Equal(OrderStatus.Processing, order.StatusHistory[1].FromStatus);
        Assert.Equal(OrderStatus.PendingPayment, order.StatusHistory[1].ToStatus);
    }

    [Fact]
    public void MarkReadyForPayment_WhenNotProcessing_IsNoOp()
    {
        // Arrange
        var order = CreateOrder();
        order.MarkReadyForPayment(Guid.NewGuid(), null, "url", null, ChangedById, "System");
        var historyCountAfterFirst = order.StatusHistory.Count;

        // Act
        order.MarkReadyForPayment(Guid.NewGuid(), null, "other", null, ChangedById, "System");

        // Assert
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(historyCountAfterFirst, order.StatusHistory.Count);
    }

    [Fact]
    public void MarkPaid_WhenPendingPayment_MovesToConfirmed()
    {
        // Arrange
        var order = CreatePendingPaymentOrder();

        // Act
        order.MarkPaid("sess-123", ChangedById, "Payer");

        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("sess-123", order.PaymentReference);
        Assert.Contains(order.StatusHistory, h =>
            h.FromStatus == OrderStatus.PendingPayment
            && h.ToStatus == OrderStatus.Confirmed
            && h.Note == "Payment completed");
    }

    [Fact]
    public void MarkPaid_WhenCancelled_IsNoOp()
    {
        // Arrange
        var order = CreatePendingPaymentOrder();
        order.Cancel("user cancelled", ChangedById, "User");
        var historyCount = order.StatusHistory.Count;

        // Act
        order.MarkPaid("sess-123", ChangedById, "Payer");

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(historyCount, order.StatusHistory.Count);
    }

    [Fact]
    public void MarkPaid_WhenConfirmedAndPaid_IsNoOp()
    {
        // Arrange
        var order = CreatePendingPaymentOrder();
        order.MarkPaid("sess-123", ChangedById, "Payer");
        var historyCount = order.StatusHistory.Count;

        // Act
        order.MarkPaid("sess-456", ChangedById, "Payer");

        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal("sess-123", order.PaymentReference);
        Assert.Equal(historyCount, order.StatusHistory.Count);
    }

    [Fact]
    public void MarkPaid_WhenProcessing_ThrowsDomainException()
    {
        // Arrange
        var order = CreateOrder();

        // Act & Assert
        var ex = Assert.Throws<OrderExceptions.CannotMarkPaidInStatus>(() =>
            order.MarkPaid("sess-123", ChangedById, "Payer"));
        Assert.Equal("Order.InvalidStatus", ex.Title);
    }

    [Fact]
    public void Cancel_CalledTwice_AddsSingleCancelHistoryEntry()
    {
        // Arrange
        var order = CreateOrder();

        // Act
        order.Cancel("reason", ChangedById, "User");
        order.Cancel("reason again", ChangedById, "User");

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(order.StatusHistory, h => h.ToStatus == OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(OrderStatus.Processing, true)]
    [InlineData(OrderStatus.PendingPayment, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CanBeCancelledBy_RespectsStatusAndOwnership(string status, bool expected)
    {
        // Arrange
        var order = CreateOrder();
        if (status == OrderStatus.PendingPayment)
            order.MarkReadyForPayment(Guid.NewGuid(), null, "url", null, ChangedById, "System");
        else if (status == OrderStatus.Confirmed)
        {
            order.MarkReadyForPayment(Guid.NewGuid(), null, "url", null, ChangedById, "System");
            order.MarkPaid("sess", ChangedById, "Payer");
        }
        else if (status == OrderStatus.Cancelled)
            order.Cancel("done", ChangedById, "User");

        // Act
        var canCancel = order.CanBeCancelledBy(UserId);

        // Assert
        Assert.Equal(expected, canCancel);
    }

    [Fact]
    public void CanBeCancelledBy_WhenDifferentUser_ReturnsFalse()
    {
        // Arrange
        var order = CreateOrder();

        // Act
        var canCancel = order.CanBeCancelledBy(Guid.NewGuid());

        // Assert
        Assert.False(canCancel);
    }

    private static OrderEntity CreatePendingPaymentOrder()
    {
        var order = CreateOrder();
        order.MarkReadyForPayment(Guid.NewGuid(), null, "url", null, ChangedById, "System");
        return order;
    }

    private static OrderEntity CreateOrder(
        Guid? orderId = null,
        decimal originalPrice = 100m,
        decimal saleDiscount = 0m,
        decimal couponDiscount = 0m)
    {
        var items = new[]
        {
            new NewOrderItemSpec(
                Guid.NewGuid(), "Product", "product-slug",
                Guid.NewGuid(), "SKU-001", "Variant",
                Guid.NewGuid(), "Seller",
                50m, 2, 0m, null)
        };

        var address = ShippingAddress.Create(
            "123 Street", null, "District 1", "Ho Chi Minh", null,
            "VN", null, "Recipient", "0900000000");

        return OrderEntity.Create(
            orderId ?? Guid.NewGuid(),
            "ORD-TEST-001",
            UserId,
            "user@example.com",
            "Test User",
            "0900000000",
            address,
            shippingFee: 0m,
            couponCode: null,
            couponDiscount: couponDiscount,
            saleDiscount: saleDiscount,
            originalPrice: originalPrice,
            customerNote: null,
            idempotencyKey: "idem-key-1",
            items: items);
    }
}
