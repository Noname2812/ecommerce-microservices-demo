using Moq;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Services.Order.UnitTests.Application.Command;

public sealed class PlaceSalesOrderCommandHandlerTests
{
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly Mock<IPendingOrderSlotService> _pendingSlots = new();
    private readonly Mock<IFlashSaleStockService> _flashSaleStock = new();
    private readonly Mock<IUserContext> _userContext = new();

    private PlaceSalesOrderCommandHandler CreateSut() =>
        new(_publisher.Object, _pendingSlots.Object, _flashSaleStock.Object, _userContext.Object);

    private static PlaceSalesOrderCommand ValidCommand(int quantity = 1) => new(
        CampaignId: Guid.NewGuid(),
        ShippingAddress: new("Nguyen Van A", "0912345678",
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null),
        ShippingFee: 30_000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString("D"),
        PricingSnapshot: new(DateTimeOffset.UtcNow),
        ExpectedTotal: 130_000,
        Items:
        [
            new(Guid.NewGuid(), "Product A", null, Guid.NewGuid(),
                "SKU-001", null, Guid.NewGuid(), "Seller A", 100_000, quantity, 0, null)
        ]);

    [Fact]
    public async Task Handle_ValidInput_ReturnsTicketId_ReservesStock_PublishesEvent()
    {
        var userId = Guid.NewGuid();
        var cmd = ValidCommand();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _flashSaleStock
            .Setup(x => x.TryReserveAsync(cmd.CampaignId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _flashSaleStock.Verify(
            x => x.TryReserveAsync(cmd.CampaignId, 1, It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            x => x.PublishAsync(
                It.Is<PlaceSalesOrderRequestedV1>(e =>
                    e.OrderId == result.Value
                    && e.CampaignId == cmd.CampaignId
                    && e.ExpectedTotal == cmd.ExpectedTotal),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_StockInsufficient_ReturnsSoldOut_ReleasesSlot_DoesNotPublish()
    {
        var userId = Guid.NewGuid();
        var cmd = ValidCommand();
        var soldOut = OrderErrors.FlashSaleSoldOut(cmd.CampaignId);
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _flashSaleStock
            .Setup(x => x.TryReserveAsync(cmd.CampaignId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(soldOut));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(soldOut.Code, result.Error.Code);
        _pendingSlots.Verify(
            x => x.ReleaseAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            x => x.PublishAsync(It.IsAny<PlaceSalesOrderRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PendingLimitHit_ReturnsTooManyPending_DoesNotReserveStock()
    {
        var userId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(OrderErrors.TooManyPendingOrders));

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.TooManyPendingOrders.Code, result.Error.Code);
        _flashSaleStock.Verify(
            x => x.TryReserveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PublishFails_RestoresStockAndSlot()
    {
        var userId = Guid.NewGuid();
        var cmd = ValidCommand(quantity: 2);
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _flashSaleStock
            .Setup(x => x.TryReserveAsync(cmd.CampaignId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _publisher
            .Setup(x => x.PublishAsync(It.IsAny<PlaceSalesOrderRequestedV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().Handle(cmd, CancellationToken.None));

        _flashSaleStock.Verify(x => x.RestoreAsync(cmd.CampaignId, 2, It.IsAny<CancellationToken>()), Times.Once);
        _pendingSlots.Verify(
            x => x.ReleaseAsync(userId, OrderType.Sales, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
