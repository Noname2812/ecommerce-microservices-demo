using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contract.Messaging.Order;
using Shared.Contract.Messaging.Payment;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Contract.Messaging.PlaceOrderSaga;
using SalesOrderItemSnapshot = Shared.Contract.Messaging.PlaceOrderSaga.OrderItemSnapshot;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using OrderEntity = UrbanX.Order.Domain.Models.Order;
using OrderDtos = Shared.Contract.Dtos.Order.OrderDtos;

namespace UrbanX.Services.Order.UnitTests.Application.Sagas;

public sealed class PlaceSalesOrderSagaStateMachineTests : IAsyncLifetime
{
    private readonly Mock<ICatalogServiceClient>      _catalog         = new();
    private readonly Mock<ISaleEligibilityService>    _saleEligibility = new();
    private readonly Mock<ICouponLockService>         _couponLock      = new();
    private readonly Mock<IFlashSaleStockService>     _flashStock      = new();
    private readonly Mock<IOrderRepository>           _orderRepo       = new();
    private readonly Mock<IUnitOfWork>                _unitOfWork      = new();
    private readonly Mock<IPendingOrderSlotService>   _slotService     = new();

    private ServiceProvider _provider = null!;
    private ITestHarness    _harness  = null!;
    private ISagaStateMachineTestHarness<PlaceSalesOrderSagaStateMachine, PlaceSalesOrderSagaState>
        _sagaHarness = null!;

    private static readonly Guid   CampaignId = Guid.NewGuid();
    private static readonly Guid   SellerId   = Guid.NewGuid();
    private static readonly Guid   ProductId  = Guid.NewGuid();
    private static readonly Guid   VariantId  = Guid.NewGuid();
    private const decimal          UnitPrice       = 100_000m;
    private const decimal          Shipping        = 30_000m;
    private const decimal          CouponDiscount  = 10_000m;
    private const string           UserId          = "11111111-1111-1111-1111-111111111111";
    private const string           CouponCode      = "SAVE10";

    public async Task InitializeAsync()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>(async (op, _) => await op());

        _slotService
            .Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _flashStock
            .Setup(s => s.RestoreAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<PlaceSalesOrderSagaStateMachine, PlaceSalesOrderSagaState>()
                    .InMemoryRepository();
            })
            .AddSingleton<IOptions<OrderPaymentOptions>>(_ => Options.Create(new OrderPaymentOptions
            {
                StepTimeoutSeconds       = 30,
                SalesOrderExpiryMinutes  = 15
            }))
            .AddSingleton<IOptions<PlaceOrderOptions>>(_ => Options.Create(new PlaceOrderOptions
            {
                PriceMismatchTolerance = 0.01m
            }))
            .AddSingleton(_catalog.Object)
            .AddSingleton(_saleEligibility.Object)
            .AddSingleton(_couponLock.Object)
            .AddSingleton(_flashStock.Object)
            .AddSingleton(_orderRepo.Object)
            .AddSingleton<IUnitOfWork>(_unitOfWork.Object)
            .AddSingleton(_slotService.Object)
            .AddSingleton<IPublishEndpoint>(sp => sp.GetRequiredService<IBus>())
            .BuildServiceProvider(true);

        _harness     = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<
            PlaceSalesOrderSagaStateMachine, PlaceSalesOrderSagaState>();

        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    private static PlaceSalesOrderRequestedV1 BuildRequest(
        Guid orderId,
        decimal expectedTotal,
        string? couponCode = null,
        string? userId = null)
    {
        var items = new[] { new SalesOrderItemSnapshot(ProductId, VariantId, 1, UnitPrice) };
        var subtotal = UnitPrice;
        return new PlaceSalesOrderRequestedV1
        {
            OrderId         = orderId,
            CorrelationId   = orderId.ToString("D"),
            UserId          = userId ?? UserId,
            CampaignId      = CampaignId,
            IdempotencyKey  = Guid.NewGuid().ToString("D"),
            ExpectedTotal   = expectedTotal,
            CouponCode      = couponCode,
            Subtotal        = subtotal,
            ShippingFee     = Shipping,
            CustomerEmail   = "test@example.com",
            CustomerName    = "Nguyen Van A",
            CustomerPhone   = "0912345678",
            Items           = items,
            ShippingAddress = new OrderDtos.ShippingAddressSnapshot(
                "Nguyen Van A", "0912345678", "123 Le Loi", "", "District 1", "Ho Chi Minh", "", "VN", null),
            PricingSnapshot = new PricingSnapshot(subtotal, Shipping, subtotal + Shipping)
        };
    }

    private static CatalogVariantInfo ActiveVariant() => new(
        ProductId, "Product A", true,
        VariantId, "SKU-001", null, true,
        UnitPrice,
        SellerId, "Seller A", true,
        null);

    private void SetupHappyValidationMocks()
    {
        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _saleEligibility.Setup(s => s.ValidateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SaleEligibilityItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SaleEligibility(
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(1),
                0m,
                CouponDiscountTypes.Fixed)));

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);
    }

    private void SetupCouponLockSuccess()
    {
        _couponLock.Setup(c => c.TryLockAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CouponLockInfo(CouponDiscount, CouponDiscountTypes.Fixed)));

        _couponLock.Setup(c => c.ConfirmUseAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _couponLock.Setup(c => c.ReleaseAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void WireOrderRepository(Guid orderId)
    {
        var order = BuildSalesOrder(orderId);
        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
    }

    private static OrderEntity BuildSalesOrder(Guid orderId)
    {
        var shipping = UrbanX.Order.Domain.ValueObjects.ShippingAddress.Create(
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null, "Nguyen Van A", "0912345678");

        return OrderEntity.Create(
            orderId,
            $"SAL-{orderId:N}",
            Guid.Parse(UserId),
            "test@example.com",
            "Nguyen Van A",
            "0912345678",
            shipping,
            Shipping,
            CouponCode,
            CouponDiscount,
            saleDiscount: 0m,
            originalPrice: UnitPrice,
            null,
            Guid.NewGuid().ToString("D"),
            [new NewOrderItemSpec(
                ProductId, "Product A", null,
                VariantId, "SKU-001", null,
                SellerId, "Seller A",
                UnitPrice, 1, 0m, null)],
            orderType: OrderType.Sales,
            campaignId: CampaignId);
    }

    private static decimal TotalWithCoupon() => UnitPrice + Shipping - CouponDiscount;

    private async Task<Guid> AdvanceToPaymentPendingAsync(
        Guid orderId,
        bool withCoupon = false,
        CancellationToken ct = default)
    {
        var expectedTotal = withCoupon ? TotalWithCoupon() : UnitPrice + Shipping;
        var couponCode    = withCoupon ? CouponCode : null;

        SetupHappyValidationMocks();
        if (withCoupon)
            SetupCouponLockSuccess();

        WireOrderRepository(orderId);

        await _harness.Bus.Publish(BuildRequest(orderId, expectedTotal, couponCode), ct);

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.InventoryReserving,
            timeout: TimeSpan.FromSeconds(5)));

        var reservationId = Guid.NewGuid();
        await _harness.Bus.Publish(new InventoryReservedV1
        {
            OrderId       = orderId,
            ReservationId = reservationId,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
            Items         = [new InventoryReserveItem(ProductId, VariantId, 1)]
        }, ct);

        Assert.True(await _harness.Published.Any<CreatePaymentSessionV1>(
            x => x.Context.Message.OrderId == orderId, ct));

        await _harness.Bus.Publish(new PaymentSessionCreatedV1
        {
            OrderId          = orderId,
            PaymentSessionId = $"ps-{orderId:N}",
            PaymentUrl       = "https://payment.example.com/pay",
            QrCodeUrl        = null,
            ExpiresAt        = DateTimeOffset.UtcNow.AddMinutes(30)
        }, ct);

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.PaymentPending, timeout: TimeSpan.FromSeconds(5)));

        return reservationId;
    }

    [Fact]
    public async Task HappyPath_ValidationPasses_TransitionsToInventoryReserving()
    {
        var orderId = Guid.NewGuid();
        var expectedTotal = UnitPrice + Shipping; // 130_000 — matches server calc (no sale/coupon discount)

        SetupHappyValidationMocks();

        await _harness.Bus.Publish(BuildRequest(orderId, expectedTotal));

        Assert.True(await _harness.Published.Any<ReserveInventoryRequestedV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.InventoryReserving,
            timeout: TimeSpan.FromSeconds(5)));

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PriceMismatch_RestoresFlashStock_ReleasesSlot_DoesNotPublishOrderCancelled()
    {
        var orderId = Guid.NewGuid();
        SetupHappyValidationMocks();

        await _harness.Bus.Publish(BuildRequest(orderId, expectedTotal: 999_999m));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Empty(await _harness.Published.SelectAsync<OrderIntegrationEvents.OrderCancelledV1>().ToListAsync());
        _orderRepo.Verify(r => r.Add(It.IsAny<OrderEntity>()), Times.Never);
    }

    [Fact]
    public async Task IdempotencyConflict_FailFast_SkipsCatalog_RestoresStock()
    {
        var orderId       = Guid.NewGuid();
        var existingOrder = OrderEntity.Create(
            Guid.NewGuid(),
            "SAL-EXISTING",
            Guid.Parse(UserId),
            "test@example.com",
            "Nguyen Van A",
            "0912345678",
            UrbanX.Order.Domain.ValueObjects.ShippingAddress.Create(
                "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null, "Nguyen Van A", "0912345678"),
            Shipping,
            null, 0m, 0m, UnitPrice,
            null,
            Guid.NewGuid().ToString("D"),
            [new NewOrderItemSpec(
                ProductId, "Product A", null,
                VariantId, "SKU-001", null,
                SellerId, "Seller A",
                UnitPrice, 1, 0m, null)],
            orderType: OrderType.Sales,
            campaignId: CampaignId);

        var request = BuildRequest(orderId, UnitPrice + Shipping);
        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(request.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        await _harness.Bus.Publish(request);

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _catalog.Verify(
            c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvalidUserId_RestoresFlashStock_ReleasesSlot()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(BuildRequest(orderId, UnitPrice + Shipping, userId: "not-a-guid"));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        _slotService.Verify(
            s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaleEligibilityFail_RestoresStock_PublishesCancelledWithHumanMessage()
    {
        var orderId = Guid.NewGuid();
        var saleMessage = OrderErrors.SaleExpired.Message;

        SetupHappyValidationMocks();
        _saleEligibility.Setup(s => s.ValidateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SaleEligibilityItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SaleEligibility>(OrderErrors.SaleExpired));

        await _harness.Bus.Publish(BuildRequest(orderId, UnitPrice + Shipping));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.True(await _sagaHarness.Sagas.Any(
            x => x.CorrelationId == orderId && x.FailureReason == saleMessage,
            CancellationToken.None));
    }

    [Fact]
    public async Task CouponLockFail_RestoresStock_DoesNotCallReleaseCoupon()
    {
        var orderId = Guid.NewGuid();

        SetupHappyValidationMocks();
        _couponLock.Setup(c => c.TryLockAsync("SAVE10", Guid.Parse(UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CouponLockInfo>(OrderErrors.CouponConcurrentClaim));

        await _harness.Bus.Publish(BuildRequest(orderId, UnitPrice + Shipping, couponCode: "SAVE10"));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        _couponLock.Verify(
            c => c.ReleaseAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HappyPath_PaymentCompleted_WithCoupon_ConfirmsCouponAndFinalizes()
    {
        var orderId         = Guid.NewGuid();
        var reservationId   = await AdvanceToPaymentPendingAsync(orderId, withCoupon: true);
        var paymentSessionId = $"ps-{orderId:N}";
        var order           = await _orderRepo.Object.GetByIdAsync(orderId, CancellationToken.None);
        Assert.NotNull(order);

        await _harness.Bus.Publish(new PaymentSessionCompletedV1
        {
            OrderId          = orderId,
            PaymentSessionId = paymentSessionId,
            AmountPaid       = TotalWithCoupon(),
            PaidAt           = DateTimeOffset.UtcNow
        });

        Assert.True(await _harness.Published.Any<ConfirmInventoryRequestedV1>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.ReservationId == reservationId,
            CancellationToken.None));

        Assert.True(await _harness.Published.Any<OrderConfirmedV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        Assert.Equal(OrderStatus.Confirmed, order!.Status);

        _couponLock.Verify(
            c => c.ConfirmUseAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()),
            Times.Once);

        _couponLock.Verify(
            c => c.ReleaseAsync(CouponCode, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _flashStock.Verify(
            s => s.RestoreAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PaymentExpiry_WithCoupon_ReleasesCouponRestoresStockPublishesCancelled()
    {
        var orderId       = Guid.NewGuid();
        var reservationId = await AdvanceToPaymentPendingAsync(orderId, withCoupon: true);

        await _harness.Bus.Publish(new PaymentExpiryTimeoutV1 { OrderId = orderId });

        Assert.True(await _harness.Published.Any<InventoryReleaseRequestedV1>(
            x => x.Context.Message.ReservationId == reservationId,
            CancellationToken.None));

        _couponLock.Verify(
            c => c.ReleaseAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()),
            Times.Once);

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.Reason == "Payment expired",
            CancellationToken.None));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InventoryReserveFailed_WithCoupon_ReleasesCouponRestoresStockCancelsOrder()
    {
        var orderId = Guid.NewGuid();

        SetupHappyValidationMocks();
        SetupCouponLockSuccess();
        WireOrderRepository(orderId);

        await _harness.Bus.Publish(BuildRequest(orderId, TotalWithCoupon(), CouponCode));

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.InventoryReserving,
            timeout: TimeSpan.FromSeconds(5)));

        await _harness.Bus.Publish(new InventoryReserveFailedV1
        {
            OrderId          = orderId,
            ErrorCode        = "OUT_OF_STOCK",
            ErrorMessage     = "Variant is out of stock",
            OutOfStockProducts = []
        });

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.Faulted, timeout: TimeSpan.FromSeconds(5)));

        _couponLock.Verify(
            c => c.ReleaseAsync(CouponCode, Guid.Parse(UserId), It.IsAny<CancellationToken>()),
            Times.Once);

        _flashStock.Verify(
            s => s.RestoreAsync(CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.Reason == "Variant is out of stock",
            CancellationToken.None));

        Assert.Empty(await _harness.Published.SelectAsync<InventoryReleaseRequestedV1>().ToListAsync());

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
