using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Application;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.Reserve;

public sealed class ReserveInventoryCommandHandlerTests
{
    private readonly Mock<IInventoryItemRepository> _items = new();
    private readonly Mock<IInventoryReservationRepository> _reservations = new();
    private readonly Mock<IEventPublisher> _eventPublisher = new();

    private ReserveInventoryCommandHandler CreateHandler() =>
        new(_items.Object, _reservations.Object, _eventPublisher.Object);

    [Fact]
    public async Task Handle_WhenActiveReservationExists_ReturnsSuccessWithoutReserving()
    {
        var orderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var existing = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            DateTimeOffset.UtcNow.AddMinutes(30),
            DateTimeOffset.UtcNow);
        existing.OrderId = orderId;

        _reservations
            .Setup(r => r.GetActiveReservationsByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var result = await CreateHandler().Handle(
            new ReserveInventoryCommand(orderId, 15, [new ReserveInventoryLineItem(Guid.NewGuid(), 2)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _items.Verify(
            i => i.GetItemIdsByVariantIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStockSufficient_ReservesAndPublishesInventoryReserved()
    {
        var orderId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var variantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var itemId = Guid.Parse("44444444-4444-4444-8444-444444444444");

        _reservations
            .Setup(r => r.GetActiveReservationsByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _items
            .Setup(i => i.GetItemIdsByVariantIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == variantId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [variantId] = itemId });

        _items
            .Setup(i => i.TryReserveAtomicAsync(itemId, 3, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        List<InventoryReservation>? added = null;
        _reservations
            .Setup(r => r.AddRange(It.IsAny<IEnumerable<InventoryReservation>>()))
            .Callback<IEnumerable<InventoryReservation>>(rows => added = rows.ToList());

        var result = await CreateHandler().Handle(
            new ReserveInventoryCommand(orderId, 15, [new ReserveInventoryLineItem(variantId, 3)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Single(added!);
        Assert.Equal(3, added![0].Quantity);
        Assert.Equal(orderId, added[0].OrderId);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<InventoryReservedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
