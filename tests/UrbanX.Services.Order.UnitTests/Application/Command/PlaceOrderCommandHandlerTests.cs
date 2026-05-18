using Moq;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Services.Order.UnitTests.Application.Command;

public sealed class PlaceOrderCommandHandlerTests
{
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly Mock<IPendingOrderSlotService> _pendingSlots = new();
    private readonly Mock<IUserContext> _userContext = new();

    private PlaceOrderCommandHandler CreateSut() =>
        new(_publisher.Object, _pendingSlots.Object, _userContext.Object);

    private static PlaceOrderCommand ValidCommand() => new(
        ShippingAddress: new("Nguyen Van A", "0912345678",
            "123 Le Loi", null, "District 1", "Ho Chi Minh", null, "VN", null),
        ShippingFee: 30_000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString("D"),
        PricingSnapshot: new(DateTimeOffset.UtcNow),
        Items:
        [
            new(Guid.NewGuid(), "Product A", null, Guid.NewGuid(),
                "SKU-001", null, Guid.NewGuid(), "Seller A", 100_000, 1, 0, null)
        ]);

    [Fact]
    public async Task Handle_ValidInput_ReturnsTicketId_PublishesEvent_AcquiresSlot()
    {
        var userId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        _pendingSlots.Verify(
            x => x.TryAcquireAsync(userId, OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            x => x.PublishAsync(
                It.Is<PlaceOrderRequestedV1>(e =>
                    e.OrderId == result.Value
                    && e.UserId == userId.ToString("D")
                    && e.IdempotencyKey == cmd.IdempotencyKey
                    && e.Items.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PendingLimitHit_ReturnsTooManyPending_DoesNotPublish()
    {
        var userId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(OrderErrors.TooManyPendingOrders));

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.TooManyPendingOrders.Code, result.Error.Code);
        _publisher.Verify(
            x => x.PublishAsync(It.IsAny<PlaceOrderRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PublishFails_ReleasesNormalSlot()
    {
        var userId = Guid.NewGuid();
        _userContext.Setup(x => x.UserId).Returns(userId);
        _pendingSlots
            .Setup(x => x.TryAcquireAsync(userId, OrderType.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _publisher
            .Setup(x => x.PublishAsync(It.IsAny<PlaceOrderRequestedV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().Handle(ValidCommand(), CancellationToken.None));

        _pendingSlots.Verify(
            x => x.ReleaseAsync(userId, OrderType.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ReturnsForbidden_DoesNotAcquireSlot()
    {
        _userContext.Setup(x => x.UserId).Returns((Guid?)null);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.Forbidden.Code, result.Error.Code);
        _pendingSlots.Verify(
            x => x.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
