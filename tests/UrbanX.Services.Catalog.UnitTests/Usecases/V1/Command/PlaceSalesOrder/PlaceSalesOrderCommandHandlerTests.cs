using Moq;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.PlaceSalesOrder;

public class PlaceSalesOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository>          _orderRepo       = new();
    private readonly Mock<IOutboxWriter>             _outboxWriter    = new();
    private readonly Mock<ICompensationOutboxWriter> _compOutbox      = new();
    private readonly Mock<IUserContext>              _userContext      = new();
    private readonly Mock<IInventoryClient>          _inventoryClient = new();
    private readonly Mock<ICouponClient>             _couponClient    = new();
    private readonly Mock<IPromotionServiceClient>   _promotionClient = new();
    private readonly Mock<IProductValidator>         _productVal      = new();
    private readonly Mock<IShippingValidator>        _shippingVal     = new();
    private readonly Mock<ISaleEligibilityValidator> _eligibilityVal  = new();
    private readonly Mock<ISaleAllocationGate>       _allocationGate  = new();
    private readonly Mock<ISalePricingValidator>     _salePricingVal  = new();
    private readonly Mock<ICacheService>             _cache           = new();
    private readonly PlaceOrderCompensationContext   _orderCompCtx    = new();
    private readonly PlaceSalesOrderCompensationContext _salesCompCtx = new();

    public PlaceSalesOrderCommandHandlerTests()
    {
        _cache.Setup(x => x.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private PlaceSalesOrderCommandHandler BuildHandler() => new(
        _orderRepo.Object, _outboxWriter.Object, _compOutbox.Object,
        _userContext.Object, _inventoryClient.Object, _couponClient.Object,
        _promotionClient.Object, _productVal.Object, _shippingVal.Object,
        _eligibilityVal.Object, _allocationGate.Object, _salePricingVal.Object,
        _cache.Object, _orderCompCtx, _salesCompCtx);

    private static PlaceSalesOrderCommand BuildValidCommand(Guid userId, Guid campaignId) => new(
        UserId: userId,
        CampaignId: campaignId,
        ShippingAddress: new("Nguyen Van A", "0912345678", "123 Le Loi", null, "D1", "HCM", null, "VN", null),
        ShippingFee: 30000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString("D"),
        PricingSnapshot: new(DateTimeOffset.UtcNow.AddMinutes(-1)),
        Items: [new(Guid.NewGuid(), "Product A", null, Guid.NewGuid(), "SKU-001", null, Guid.NewGuid(), "Seller", 100_000, 1, 0, null)]
    );

    private void SetupHappyPath(Guid userId, Guid campaignId)
    {
        _userContext.Setup(x => x.UserId).Returns(userId);
        _eligibilityVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _allocationGate.Setup(x => x.TryReserveAsync(
            campaignId, userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success($"sale:{campaignId}:quota"));
        _productVal.Setup(x => x.ValidateAsync(
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _shippingVal.Setup(x => x.ValidateAsync(
            It.IsAny<PlaceOrderShippingAddressDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _salePricingVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<PlaceOrderPricingSnapshotDto>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _inventoryClient.Setup(x => x.ReserveAsync(
            It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReserveResponse(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10), []));
        _outboxWriter.Setup(x => x.WriteAsync(
            It.IsAny<PlaceSalesOrderConfirmedV1>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidSalesOrder_ReturnsSuccessWithOrderId()
    {
        var userId     = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        SetupHappyPath(userId, campaignId);
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<PlaceSalesOrderConfirmedV1>(e => e.CampaignId == campaignId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserIdMismatch_ReturnsForbidden()
    {
        var userId     = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(Guid.NewGuid());
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_NullUserId_ReturnsForbidden()
    {
        var campaignId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns((Guid?)null);
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(Guid.NewGuid(), campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_EligibilityFailed_ReturnsError_DoesNotCallAllocationGate()
    {
        var userId     = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var eligError  = OrderErrors.SaleCampaignInvalid("Not eligible");
        _userContext.Setup(x => x.UserId).Returns(userId);
        _eligibilityVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(eligError));
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        _allocationGate.Verify(x => x.TryReserveAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_QuotaExceeded_ReturnsSaleQuotaExceeded_DoesNotCallInventory()
    {
        var userId     = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _eligibilityVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _allocationGate.Setup(x => x.TryReserveAsync(
            campaignId, userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(OrderErrors.SaleQuotaExceeded));
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderErrors.SaleQuotaExceeded.Code, result.Error.Code);
        _inventoryClient.Verify(x => x.ReserveAsync(
            It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PriceMismatch_SetsCompensationContext_ReturnsError()
    {
        var userId     = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var pricingErr = OrderErrors.SaleWindowExpired;
        _userContext.Setup(x => x.UserId).Returns(userId);
        _eligibilityVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _allocationGate.Setup(x => x.TryReserveAsync(
            campaignId, userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success($"sale:{campaignId}:quota"));
        _productVal.Setup(x => x.ValidateAsync(
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _shippingVal.Setup(x => x.ValidateAsync(
            It.IsAny<PlaceOrderShippingAddressDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _salePricingVal.Setup(x => x.ValidateAsync(
            It.IsAny<Guid>(), It.IsAny<PlaceOrderPricingSnapshotDto>(),
            It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(pricingErr));
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(_salesCompCtx.SaleQuotaKey);
    }

    [Fact]
    public async Task Handle_Success_OutboxEventIsSalesConfirmed()
    {
        var userId        = Guid.NewGuid();
        var campaignId    = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        SetupHappyPath(userId, campaignId);
        _inventoryClient.Setup(x => x.ReserveAsync(
            It.IsAny<ReserveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReserveResponse(reservationId, DateTimeOffset.UtcNow.AddMinutes(10), []));
        var handler = BuildHandler();
        var cmd     = BuildValidCommand(userId, campaignId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<PlaceSalesOrderConfirmedV1>(e =>
                e.CampaignId    == campaignId &&
                e.ReservationId == reservationId &&
                e.UserId        == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
