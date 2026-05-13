using Moq;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Exceptions;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceOrder;

public class PlaceOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<ICompensationOutboxWriter> _compensationOutbox = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<IInventoryClient> _inventoryClient = new();
    private readonly Mock<ICouponClient> _couponClient = new();
    private readonly Mock<IPromotionServiceClient> _promotion = new();
    private readonly Mock<IProductValidator> _productValidator = new();
    private readonly Mock<IShippingValidator> _shippingValidator = new();
    private readonly Mock<IPricingValidator> _pricingValidator = new();
    private readonly PlaceOrderCompensationContext _compensationContext = new();

    private PlaceOrderCommandHandler CreateHandler() => new(
        _orderRepository.Object,
        _outbox.Object,
        _compensationOutbox.Object,
        _userContext.Object,
        _inventoryClient.Object,
        _couponClient.Object,
        _promotion.Object,
        _productValidator.Object,
        _shippingValidator.Object,
        _pricingValidator.Object,
        _compensationContext);

    private void SetupAllBusinessValidatorsSuccess()
    {
        _productValidator
            .Setup(x => x.ValidateAsync(It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _shippingValidator
            .Setup(x => x.ValidateAsync(It.IsAny<PlaceOrderShippingAddressDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _pricingValidator
            .Setup(x => x.ValidateAsync(
                It.IsAny<PlaceOrderPricingSnapshotDto>(),
                It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    private Guid SetupReserveSuccess()
    {
        var reservationId = Guid.NewGuid();
        _inventoryClient
            .Setup(x => x.ReserveAsync(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReserveResponse(
                reservationId,
                DateTimeOffset.UtcNow.AddMinutes(15),
                Array.Empty<ReservedItemResponse>()));
        return reservationId;
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsForbidden()
    {
        var cmd = ValidCommand(Guid.NewGuid());
        _userContext.SetupGet(x => x.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ORDER_FORBIDDEN", result.Error.Code);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCommandUserMismatchesContext_ReturnsForbidden()
    {
        var cmd = ValidCommand(Guid.NewGuid());
        _userContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ORDER_FORBIDDEN", result.Error.Code);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
    }

    // TC-HP-001: Happy path without coupon
    [Fact]
    public async Task Handle_TC_HP_001_HappyPath_NoCoupon_ConfirmsOrderAndPublishesOutbox()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        var reservationId = SetupReserveSuccess();

        OrderEntity? saved = null;
        _orderRepository.Setup(x => x.Add(It.IsAny<OrderEntity>()))
            .Callback<OrderEntity>(o => saved = o);

        var cmd = ValidCommand(userId, couponCode: null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal(reservationId, saved!.ReservationId);
        Assert.Null(saved.CouponClaimId);
        Assert.Equal("CONFIRMED", saved.Status);
        Assert.Equal(0m, saved.CouponDiscount);
        Assert.Equal(reservationId, _compensationContext.ReservationId);
        Assert.Null(_compensationContext.CouponClaimId);

        _couponClient.Verify(
            x => x.ClaimAsync(It.IsAny<ClaimCouponRequest>(), It.IsAny<CouponClaimReservationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _promotion.Verify(
            x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _outbox.Verify(
            x => x.WriteAsync(
                It.Is<Shared.Contract.Messaging.PlaceOrder.OrderConfirmedForPlaceOrderV1>(e =>
                    e.ReservationId == reservationId &&
                    e.ClaimId == null &&
                    e.UserId == userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // TC-HP-002: Happy path with coupon
    [Fact]
    public async Task Handle_TC_HP_002_HappyPath_WithCoupon_AppliesDiscountAndClaimsCoupon()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        var reservationId = SetupReserveSuccess();

        var item = ValidItem();
        var promotionResponse = new PromotionRedeemResponse(
            OrderLevelDiscount: 10_000,
            ItemDiscounts: new List<PromotionItemDiscount>
            {
                new(item.VariantId, 1_000)
            },
            AppliedPromotionIds: new[] { Guid.NewGuid() });
        _promotion
            .Setup(x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(promotionResponse));

        var claimId = Guid.NewGuid();
        _couponClient
            .Setup(x => x.ClaimAsync(
                It.IsAny<ClaimCouponRequest>(),
                It.IsAny<CouponClaimReservationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimCouponResponse(claimId, 10_000, DateTimeOffset.UtcNow.AddHours(1)));

        OrderEntity? saved = null;
        _orderRepository.Setup(x => x.Add(It.IsAny<OrderEntity>()))
            .Callback<OrderEntity>(o => saved = o);

        var cmd = ValidCommand(userId, couponCode: "SPRING-2026", item: item);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal(reservationId, saved!.ReservationId);
        Assert.Equal(claimId, saved.CouponClaimId);
        Assert.Equal("CONFIRMED", saved.Status);
        Assert.Equal(10_000m, saved.CouponDiscount);
        Assert.Equal("SPRING-2026", saved.CouponCode);
        Assert.Single(saved.Items);
        Assert.Equal(1_000m * item.Quantity, saved.Items[0].DiscountAmount);
        Assert.Equal(reservationId, _compensationContext.ReservationId);
        Assert.Equal(claimId, _compensationContext.CouponClaimId);

        _couponClient.Verify(
            x => x.ClaimAsync(
                It.Is<ClaimCouponRequest>(r =>
                    r.OrderIdempotencyKey == cmd.IdempotencyKey &&
                    r.CouponCode == "SPRING-2026" &&
                    r.UserId == userId),
                It.Is<CouponClaimReservationContext>(c => c.ReservationId == reservationId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outbox.Verify(
            x => x.WriteAsync(
                It.Is<Shared.Contract.Messaging.PlaceOrder.OrderConfirmedForPlaceOrderV1>(e =>
                    e.ReservationId == reservationId &&
                    e.ClaimId == claimId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // TC-INV-001: Inventory out of stock → 409, no coupon claim, no save
    [Fact]
    public async Task Handle_TC_INV_001_OutOfStock_ReturnsFailureAndDoesNotSaveOrder()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        _inventoryClient
            .Setup(x => x.ReserveAsync(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutOfStockException("requested 5, available 1"));

        var cmd = ValidCommand(userId, couponCode: null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("INVENTORY_OUT_OF_STOCK", result.Error.Code);
        Assert.Null(_compensationContext.ReservationId);
        Assert.Null(_compensationContext.CouponClaimId);

        _couponClient.Verify(
            x => x.ClaimAsync(It.IsAny<ClaimCouponRequest>(), It.IsAny<CouponClaimReservationContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
        _outbox.Verify(
            x => x.WriteAsync(It.IsAny<Shared.Contract.Messaging.PlaceOrder.OrderConfirmedForPlaceOrderV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // TC-INV-001 variant: Inventory unavailable → 503, no save
    [Fact]
    public async Task Handle_WhenInventoryUnavailable_ReturnsFailureAndDoesNotSaveOrder()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        _inventoryClient
            .Setup(x => x.ReserveAsync(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InventoryUnavailableException("inventory request timed out"));

        var cmd = ValidCommand(userId);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("INVENTORY_UNAVAILABLE", result.Error.Code);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
        _outbox.Verify(
            x => x.WriteAsync(It.IsAny<Shared.Contract.Messaging.PlaceOrder.OrderConfirmedForPlaceOrderV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // TC-CPN-002: Coupon fail → 409, ReservationId set on context (so behavior can compensate),
    // CouponClient itself writes IInventoryReleaseRequested before throwing.
    [Fact]
    public async Task Handle_TC_CPN_002_CouponClaimFails_ReturnsFailureAndDoesNotSaveOrder()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        var reservationId = SetupReserveSuccess();
        _promotion
            .Setup(x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PromotionRedeemResponse(
                OrderLevelDiscount: 5_000,
                ItemDiscounts: Array.Empty<PromotionItemDiscount>(),
                AppliedPromotionIds: Array.Empty<Guid>())));

        _couponClient
            .Setup(x => x.ClaimAsync(
                It.IsAny<ClaimCouponRequest>(),
                It.IsAny<CouponClaimReservationContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CouponException("COUPON_ALREADY_USED", "duplicate"));

        var cmd = ValidCommand(userId, couponCode: "DUP-CODE");

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("COUPON_CLAIM_FAILED", result.Error.Code);
        Assert.Equal(reservationId, _compensationContext.ReservationId);
        Assert.Null(_compensationContext.CouponClaimId);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
        _outbox.Verify(
            x => x.WriteAsync(It.IsAny<Shared.Contract.Messaging.PlaceOrder.OrderConfirmedForPlaceOrderV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _couponClient.Verify(
            x => x.ClaimAsync(
                It.IsAny<ClaimCouponRequest>(),
                It.Is<CouponClaimReservationContext>(c => c.ReservationId == reservationId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPromotionRedeemFails_ReturnsPromotionInvalidAndDoesNotReserve()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        SetupAllBusinessValidatorsSuccess();
        _promotion
            .Setup(x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PromotionRedeemResponse>(new Error("PROMOTION_EXPIRED", "Coupon expired")));

        var cmd = ValidCommand(userId, couponCode: "EXPIRED");

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ORDER_PROMOTION_INVALID", result.Error.Code);
        _inventoryClient.Verify(
            x => x.ReserveAsync(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orderRepository.Verify(x => x.Add(It.IsAny<OrderEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOneBusinessRuleFails_ReturnsWithoutWaitingOtherRules()
    {
        var userId = Guid.NewGuid();
        _userContext.SetupGet(x => x.UserId).Returns(userId);

        _productValidator
            .Setup(x => x.ValidateAsync(It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("PRODUCT_UNAVAILABLE", "inactive")));
        _shippingValidator
            .Setup(x => x.ValidateAsync(It.IsAny<PlaceOrderShippingAddressDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (PlaceOrderShippingAddressDto _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return Result.Success();
            });
        _pricingValidator
            .Setup(x => x.ValidateAsync(
                It.IsAny<PlaceOrderPricingSnapshotDto>(),
                It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (PlaceOrderPricingSnapshotDto _, IReadOnlyList<PlaceOrderLineDto> _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return Result.Success();
            });

        var startedAt = DateTimeOffset.UtcNow;
        var result = await CreateHandler().Handle(ValidCommand(userId), CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        Assert.True(result.IsFailure);
        Assert.Equal("PRODUCT_UNAVAILABLE", result.Error.Code);
        Assert.True(elapsed < TimeSpan.FromSeconds(1.5));
        _inventoryClient.Verify(
            x => x.ReserveAsync(It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _promotion.Verify(
            x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PlaceOrderCommand ValidCommand(
        Guid userId,
        string? couponCode = null,
        PlaceOrderLineDto? item = null) => new(
        UserId: userId,
        ShippingAddress: new PlaceOrderShippingAddressDto(
            FullName: "UrbanX User",
            Phone: "+84987654321",
            Address: "123 Main St",
            Ward: null,
            District: "District 1",
            City: "HoChiMinh",
            Province: null,
            Country: "VN",
            ZipCode: null),
        ShippingFee: 10_000,
        CouponCode: couponCode,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString(),
        PricingSnapshot: new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
        Items: [item ?? ValidItem()]);

    private static PlaceOrderLineDto ValidItem() => new(
        ProductId: Guid.NewGuid(),
        ProductName: "P",
        ProductSlug: "p",
        VariantId: Guid.NewGuid(),
        VariantSku: "SKU",
        VariantName: "Default",
        SellerId: Guid.NewGuid(),
        SellerName: "Seller",
        UnitPrice: 100_000,
        Quantity: 1,
        DiscountAmount: 0,
        ImageUrl: null);
}
