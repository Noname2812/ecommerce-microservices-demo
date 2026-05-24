using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contract.Messaging.Order;
using Shared.Contract.Messaging.Payment;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using OrderEntity = UrbanX.Order.Domain.Models.Order;
using OrderDtos = Shared.Contract.Dtos.Order.OrderDtos;
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;

namespace UrbanX.Services.Order.UnitTests.Application.Sagas;

public sealed class PlaceOrderNormalSagaStateMachineTests : IAsyncLifetime
{
    // ── Mocks ──────────────────────────────────────────────────────────────────
    private readonly Mock<ICatalogServiceClient>    _catalog         = new();
    private readonly Mock<IOrderRepository>           _orderRepo       = new();
    private readonly Mock<IUnitOfWork>                _unitOfWork      = new();
    private readonly Mock<IPendingOrderSlotService>   _slotService     = new();

    private ServiceProvider _provider = null!;
    private ITestHarness    _harness  = null!;
    private ISagaStateMachineTestHarness<PlaceOrderNormalSagaStateMachine, PlaceOrderNormalSagaState>
        _sagaHarness = null!;

    // ── Test data ──────────────────────────────────────────────────────────────
    private static readonly Guid   SellerId  = Guid.NewGuid();
    private static readonly Guid   ProductId = Guid.NewGuid();
    private static readonly Guid   VariantId = Guid.NewGuid();
    private const decimal          UnitPrice = 100_000m;
    private const string           UserId    = "11111111-1111-1111-1111-111111111111";

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // IUnitOfWork: executes the callback synchronously so repo calls actually happen
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>(async (op, _) => await op());

        _slotService
            .Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<PlaceOrderNormalSagaStateMachine, PlaceOrderNormalSagaState>()
                    .InMemoryRepository();
            })
            .AddSingleton<IOptions<OrderPaymentOptions>>(_ => Options.Create(new OrderPaymentOptions
            {
                NormalOrderExpiryMinutes = 30,
                SalesOrderExpiryMinutes  = 15
            }))
            .AddSingleton(_catalog.Object)
            .AddSingleton(_orderRepo.Object)
            .AddSingleton<IUnitOfWork>(_unitOfWork.Object)
            .AddSingleton(_slotService.Object)
            .AddSingleton<IPublishEndpoint>(sp => sp.GetRequiredService<IBus>())
            .BuildServiceProvider(true);

        _harness     = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<
            PlaceOrderNormalSagaStateMachine, PlaceOrderNormalSagaState>();

        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PlaceOrderRequestedV1 BuildRequest(Guid orderId, string? couponCode = null)
    {
        var items = new[] { new NormalOrderItemSnapshot(ProductId, VariantId, 1, UnitPrice, 1) };
        return new PlaceOrderRequestedV1
        {
            OrderId          = orderId,
            CorrelationId    = orderId.ToString("D"),
            UserId           = UserId,
            IdempotencyKey   = Guid.NewGuid().ToString("D"),
            CouponCode       = couponCode,
            Subtotal         = items.Sum(i => i.UnitPrice * i.Quantity),
            ShippingFee      = 30_000m,
            CustomerEmail    = "test@example.com",
            CustomerName     = "Nguyen Van A",
            CustomerPhone    = "0912345678",
            Items            = items,
            ShippingAddress  = new OrderDtos.ShippingAddressSnapshot(
                "Nguyen Van A", "0912345678", "123 Le Loi", "", "District 1", "Ho Chi Minh", "", "VN", null),
            PricingSnapshotJson = "{}"
        };
    }

    private static CatalogVariantInfo ActiveVariant() => new(
        ProductId, "Product A", true,
        VariantId, "SKU-001", null, true,
        UnitPrice,
        SellerId, "Seller A", true,
        null);

    private OrderEntity BuildOrder(Guid orderId)
    {
        var shipping = UrbanX.Order.Domain.ValueObjects.ShippingAddress.Create(
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null, "Nguyen Van A", "0912345678");

        return OrderEntity.Create(
            orderId,
            $"ORD-{orderId:N}",
            Guid.Parse(UserId),
            "test@example.com",
            "Nguyen Van A",
            "0912345678",
            shipping,
            30_000m,
            null, 0m, 0m, UnitPrice,
            null,
            Guid.NewGuid().ToString("D"),
            [new NewOrderItemSpec(
                ProductId, "Product A", null,
                VariantId, "SKU-001", null,
                SellerId, "Seller A",
                UnitPrice, 1, 0m, null)]);
    }

    // ── Test 1: Happy path → Requested → InventoryReserving → PaymentSessionCreating → PaymentPending → Finalized ─
    [Fact]
    public async Task HappyPath_PaymentCompleted_SagaFinalizesAndPublishesConfirmedV1()
    {
        var orderId = Guid.NewGuid();
        var order   = BuildOrder(orderId);
        var reservationId    = Guid.NewGuid();
        var paymentSessionId = $"ps-{orderId:N}";

        IEnumerable<Guid>? catalogVariantIds = null;
        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Guid>, CancellationToken>((ids, _) => catalogVariantIds = ids.ToList())
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        // Act — publish initial event
        await _harness.Bus.Publish(BuildRequest(orderId));

        // Saga should transition to InventoryReserving and publish ReserveInventoryRequestedV1
        Assert.True(await _harness.Published.Any<ReserveInventoryRequestedV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        Assert.NotNull(catalogVariantIds);
        Assert.Contains(VariantId, catalogVariantIds);

        // Simulate inventory service response
        await _harness.Bus.Publish(new InventoryReservedV1
        {
            OrderId       = orderId,
            ReservationId = reservationId,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
            Items         = [new InventoryReserveItem(ProductId, VariantId, 1)]
        });

        // Saga should publish CreatePaymentSessionV1 (WhenEnter PaymentSessionCreating)
        Assert.True(await _harness.Published.Any<CreatePaymentSessionV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        // Simulate payment service creating session
        await _harness.Bus.Publish(new PaymentSessionCreatedV1
        {
            OrderId          = orderId,
            PaymentSessionId = paymentSessionId,
            PaymentUrl       = "https://payment.example.com/pay",
            QrCodeUrl        = null,
            ExpiresAt        = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        // Saga should be in PaymentPending — verify MarkReadyForPayment was called
        var pendingInstance = await _sagaHarness.Exists(orderId, x => x.PaymentPending,
            timeout: TimeSpan.FromSeconds(5));
        Assert.NotNull(pendingInstance);

        // Simulate payment completed
        await _harness.Bus.Publish(new PaymentSessionCompletedV1
        {
            OrderId          = orderId,
            PaymentSessionId = paymentSessionId,
            AmountPaid       = UnitPrice + 30_000m,
            PaidAt           = DateTimeOffset.UtcNow
        });

        Assert.True(await _harness.Published.Any<ConfirmInventoryRequestedV1>(
            x => x.Context.Message.OrderId == orderId && x.Context.Message.ReservationId == reservationId,
            CancellationToken.None));

        Assert.True(await _harness.Published.Any<OrderConfirmedV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        Assert.Equal(OrderStatus.Confirmed, order.Status);

        // Finalized sagas leave PaymentPending; MassTransit may remove the instance from the repository.
        Assert.Null(await _sagaHarness.Exists(orderId, x => x.PaymentPending,
            timeout: TimeSpan.FromSeconds(2)));

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 2: Catalog validation fails → Faulted + OrderCancelledV1 + slot released ──────────────────────
    [Fact]
    public async Task ValidationFail_CatalogUnavailable_TransitionsFaultedPublishesCancelledReleasesSlot()
    {
        var orderId = Guid.NewGuid();

        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<CatalogVariantInfo>>(
                new Error("Order.CatalogUnavailable", "Catalog service is down")));

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await _harness.Bus.Publish(BuildRequest(orderId));

        // OrderCancelledV1 published with CATALOG_UNAVAILABLE reason
        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        var faultedInstance = await _sagaHarness.Exists(orderId, x => x.Faulted,
            timeout: TimeSpan.FromSeconds(5));
        Assert.NotNull(faultedInstance);

        // Slot released before order was created
        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);

        // Inventory was never reserved — no release published
        Assert.Empty(await _harness.Published.SelectAsync<InventoryReleaseRequestedV1>().ToListAsync());
    }

    // ── Test 3: Inventory reserve fails → Compensating → Faulted ─────────────────────────────────────────
    [Fact]
    public async Task InventoryReserveFailed_TransitionsCompensatingThenFaulted()
    {
        var orderId = Guid.NewGuid();
        var order   = BuildOrder(orderId);

        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await _harness.Bus.Publish(BuildRequest(orderId));

        // Wait until saga is in InventoryReserving
        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.InventoryReserving,
            timeout: TimeSpan.FromSeconds(5)));

        // Simulate inventory failure
        await _harness.Bus.Publish(new InventoryReserveFailedV1
        {
            OrderId          = orderId,
            ErrorCode        = "OUT_OF_STOCK",
            ErrorMessage     = "Variant is out of stock",
            OutOfStockProducts = []
        });

        // Saga transitions to Faulted via Compensating
        var faultedInstance = await _sagaHarness.Exists(orderId, x => x.Faulted,
            timeout: TimeSpan.FromSeconds(5));
        Assert.NotNull(faultedInstance);

        // OrderCancelledV1 published
        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        // Slot released
        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);

        // ReservationId was never set — no inventory release
        Assert.Empty(await _harness.Published.SelectAsync<InventoryReleaseRequestedV1>().ToListAsync());
    }

    // ── Test 4: Payment expiry → release inventory + coupon + cancel + OrderCancelledV1 + slot ────────────
    [Fact]
    public async Task PaymentExpiry_WithCoupon_ReleasesInventoryAndCouponCancelsPendingSlot()
    {
        var orderId      = Guid.NewGuid();
        var order        = BuildOrder(orderId);
        var reservationId = Guid.NewGuid();
        var claimId       = Guid.NewGuid();
        var paymentSessionId = $"ps-{orderId:N}";

        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        // Use a coupon so CouponClaiming state is entered
        await _harness.Bus.Publish(BuildRequest(orderId, couponCode: "SAVE10"));

        Assert.True(await _harness.Published.Any<ReserveInventoryRequestedV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        await _harness.Bus.Publish(new InventoryReservedV1
        {
            OrderId       = orderId,
            ReservationId = reservationId,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
            Items         = [new InventoryReserveItem(ProductId, VariantId, 1)]
        });

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.CouponClaiming,
            timeout: TimeSpan.FromSeconds(5)));

        await _harness.Bus.Publish(new CouponClaimedV1
        {
            OrderId        = orderId,
            ClaimId        = claimId,
            DiscountAmount = 10_000m,
            ExpiresAt      = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.PaymentSessionCreating,
            timeout: TimeSpan.FromSeconds(5)));

        Assert.True(await _harness.Published.Any<CreatePaymentSessionV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        // Payment session created → saga moves to PaymentPending
        await _harness.Bus.Publish(new PaymentSessionCreatedV1
        {
            OrderId          = orderId,
            PaymentSessionId = paymentSessionId,
            PaymentUrl       = "https://payment.example.com/pay",
            QrCodeUrl        = null,
            ExpiresAt        = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.PaymentPending,
            timeout: TimeSpan.FromSeconds(5)));

        // Simulate payment expiry by publishing the timeout message directly
        await _harness.Bus.Publish(new PaymentExpiryTimeoutV1 { OrderId = orderId });

        // Inventory release published
        Assert.True(await _harness.Published.Any<InventoryReleaseRequestedV1>(
            x => x.Context.Message.ReservationId == reservationId,
            CancellationToken.None));

        // Coupon release published
        Assert.True(await _harness.Published.Any<CouponReleaseRequestedV1>(
            x => x.Context.Message.ClaimId == claimId,
            CancellationToken.None));

        // OrderCancelledV1 published
        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        var faultedInstance = await _sagaHarness.Exists(orderId, x => x.Faulted,
            timeout: TimeSpan.FromSeconds(5));
        Assert.NotNull(faultedInstance);

        // Pending slot released
        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 5: Coupon claim fails after inventory reserved → release inv + cancel (no double-release) ─
    [Fact]
    public async Task CouponClaimFailed_ReleasesInventoryOnceAndCancelsOrder()
    {
        var orderId       = Guid.NewGuid();
        var order         = BuildOrder(orderId);
        var reservationId = Guid.NewGuid();

        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await _harness.Bus.Publish(BuildRequest(orderId, couponCode: "SAVE10"));

        await _harness.Bus.Publish(new InventoryReservedV1
        {
            OrderId       = orderId,
            ReservationId = reservationId,
            ExpiresAt     = DateTimeOffset.UtcNow.AddMinutes(10),
            Items         = [new InventoryReserveItem(ProductId, VariantId, 1)]
        });

        await _harness.Bus.Publish(new CouponClaimFailedV1
        {
            OrderId      = orderId,
            ErrorCode    = "COUPON_EXHAUSTED",
            ErrorMessage = "Coupon quota exhausted"
        });

        var faultedInstance = await _sagaHarness.Exists(orderId, x => x.Faulted,
            timeout: TimeSpan.FromSeconds(5));
        Assert.NotNull(faultedInstance);

        Assert.True(await _sagaHarness.Sagas.Any(
            x => x.CorrelationId == orderId && x.FailureStep == "CouponClaim",
            CancellationToken.None));

        var releases = await _harness.Published.SelectAsync<InventoryReleaseRequestedV1>().ToListAsync();
        Assert.Single(releases);
        Assert.Equal(reservationId, releases[0].Context.Message.ReservationId);

        Assert.True(await _harness.Published.Any<OrderIntegrationEvents.OrderCancelledV1>(
            x => x.Context.Message.OrderId == orderId,
            CancellationToken.None));

        _slotService.Verify(
            s => s.ReleaseAsync(Guid.Parse(UserId), OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 6: Create order is idempotent when order already exists by idempotency key ────────────────
    [Fact]
    public async Task CreateOrderProcessingAsync_WhenOrderExistsByIdempotencyKey_SkipsInsert()
    {
        var orderId = Guid.NewGuid();
        var order   = BuildOrder(orderId);
        var request = BuildRequest(orderId);

        _catalog.Setup(c => c.GetVariantsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CatalogVariantInfo>>([ActiveVariant()]));

        _orderRepo.Setup(r => r.GetByIdempotencyKeyAsync(request.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRepo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        await _harness.Bus.Publish(request);

        Assert.NotNull(await _sagaHarness.Exists(orderId, x => x.InventoryReserving,
            timeout: TimeSpan.FromSeconds(5)));

        _orderRepo.Verify(r => r.Add(It.IsAny<OrderEntity>()), Times.Never);
    }
}
