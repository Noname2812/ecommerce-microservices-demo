using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceOrder;

public class PlaceOrderCompensationBehaviorTests
{
    private readonly Mock<ICompensationOutboxWriter> _writer = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public PlaceOrderCompensationBehaviorTests()
    {
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>((op, _) => op());
    }

    private (PlaceOrderCompensationBehavior Behavior, PlaceOrderCompensationContext Context) BuildBehavior()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _writer.Object);
        services.AddScoped(_ => _unitOfWork.Object);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var context = new PlaceOrderCompensationContext();
        var behavior = new PlaceOrderCompensationBehavior(
            context,
            scopeFactory,
            NullLogger<PlaceOrderCompensationBehavior>.Instance);

        return (behavior, context);
    }

    private static RequestHandlerDelegate<Result<Guid>> SuccessNext(Guid id) =>
        _ => Task.FromResult(Result.Success(id));

    private static RequestHandlerDelegate<Result<Guid>> ThrowingNext(Exception ex) =>
        _ => throw ex;

    private static PlaceOrderCommand BuildCommand() => new(
        UserId: Guid.NewGuid(),
        ShippingAddress: new PlaceOrderShippingAddressDto(
            "U", "+84987654321", "A", null, "District 1", "HoChiMinh", null, "VN", null),
        ShippingFee: 10_000,
        CouponCode: null,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString(),
        PricingSnapshot: new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
        Items: new List<PlaceOrderLineDto>
        {
            new(Guid.NewGuid(), "P", null, Guid.NewGuid(), "SKU", null,
                Guid.NewGuid(), "Seller", 100_000, 1, 0, null)
        });

    [Fact]
    public async Task Handle_WhenNextSucceeds_DoesNotWriteCompensation_AndReturnsResult()
    {
        var (behavior, context) = BuildBehavior();
        context.ReservationId = Guid.NewGuid();
        context.CouponClaimId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = await behavior.Handle(BuildCommand(), SuccessNext(orderId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(orderId, result.Value);
        _writer.Verify(
            w => w.AddAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _writer.Verify(
            w => w.AddAsync(It.IsAny<InventoryReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _writer.Verify(
            w => w.AddAsync(It.IsAny<CouponReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsAndReservationIdMissing_DoesNotWriteCompensation()
    {
        var (behavior, _) = BuildBehavior();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(BuildCommand(), ThrowingNext(new InvalidOperationException("save failed")), CancellationToken.None));

        _writer.Verify(
            w => w.AddAsync(It.IsAny<InventoryReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _writer.Verify(
            w => w.AddAsync(It.IsAny<CouponReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsAndOnlyReservationIdSet_WritesInventoryReleaseOnly()
    {
        var (behavior, context) = BuildBehavior();
        var reservationId = Guid.NewGuid();
        context.ReservationId = reservationId;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(BuildCommand(), ThrowingNext(new InvalidOperationException("DB failed")), CancellationToken.None));

        Assert.Equal("DB failed", ex.Message);
        _writer.Verify(
            w => w.AddAsync(
                It.Is<InventoryReleaseRequestedV1>(e => e.ReservationId == reservationId && e.Reason == "ORDER_SAVE_FAILED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _writer.Verify(
            w => w.AddAsync(It.IsAny<CouponReleaseRequestedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // TC-SAVE-001: DB save failure with reservation + claim → 2 compensation events
    [Fact]
    public async Task Handle_TC_SAVE_001_WhenNextThrowsWithReservationAndClaim_WritesBothCompensationEvents()
    {
        var (behavior, context) = BuildBehavior();
        var reservationId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        context.ReservationId = reservationId;
        context.CouponClaimId = claimId;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(BuildCommand(), ThrowingNext(new InvalidOperationException("DB failed")), CancellationToken.None));

        Assert.Equal("DB failed", ex.Message);
        _writer.Verify(
            w => w.AddAsync(
                It.Is<InventoryReleaseRequestedV1>(e => e.ReservationId == reservationId && e.Reason == "ORDER_SAVE_FAILED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _writer.Verify(
            w => w.AddAsync(
                It.Is<CouponReleaseRequestedV1>(e => e.ClaimId == claimId && e.Reason == "ORDER_SAVE_FAILED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNextThrowsOperationCanceled_UsesOrderCancelledReason()
    {
        var (behavior, context) = BuildBehavior();
        var reservationId = Guid.NewGuid();
        context.ReservationId = reservationId;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            behavior.Handle(BuildCommand(), ThrowingNext(new OperationCanceledException()), CancellationToken.None));

        _writer.Verify(
            w => w.AddAsync(
                It.Is<InventoryReleaseRequestedV1>(e => e.ReservationId == reservationId && e.Reason == "ORDER_CANCELLED"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCompensationWriteThrows_OriginalExceptionStillPropagates()
    {
        var (behavior, context) = BuildBehavior();
        context.ReservationId = Guid.NewGuid();

        _writer
            .Setup(w => w.AddAsync(It.IsAny<InventoryReleaseRequestedV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("compensation write failed"));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            behavior.Handle(BuildCommand(), ThrowingNext(new ApplicationException("save failed")), CancellationToken.None));

        Assert.Equal("save failed", ex.Message);
    }
}
