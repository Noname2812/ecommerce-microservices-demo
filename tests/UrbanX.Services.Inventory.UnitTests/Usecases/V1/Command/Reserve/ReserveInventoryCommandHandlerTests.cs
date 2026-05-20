using Moq;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.Reserve;

public class ReserveInventoryCommandHandlerTests
{
    private static readonly string Idem1 = "11111111-1111-4111-8111-111111111111";
    private static readonly string IdemK = "22222222-2222-4222-8222-222222222222";
    private static readonly string IdemSame = "33333333-3333-4333-8333-333333333333";

    private readonly Mock<IInventoryItemRepository> _items = new();
    private readonly Mock<IInventoryReservationRepository> _reservations = new();

    private ReserveInventoryCommandHandler CreateHandler() =>
        new(_items.Object, _reservations.Object);

    [Fact]
    public async Task TC_INV_001_Handle_WhenStockSufficient_AtomicReserveSucceedsAndInsertsReservation()
    {
        var productId = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var itemId = Guid.Parse("20000000-0000-4000-8000-000000000001");

        _reservations
            .Setup(r => r.GetReservationsForIdempotentReplayAsync(Idem1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _items
            .Setup(r => r.GetPrimaryItemIdsByProductAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == productId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [productId] = itemId });

        _items
            .Setup(r => r.TryReserveAtomicAsync(itemId, 3, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        List<InventoryReservation>? added = null;
        _reservations
            .Setup(r => r.AddRange(It.IsAny<IEnumerable<InventoryReservation>>()))
            .Callback<IEnumerable<InventoryReservation>>(rows => added = rows.ToList());

        var handler = CreateHandler();
        var cmd = new ReserveInventoryCommand(Idem1, [new ReserveInventoryLineItem(productId, 3)]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Single(added!);
        Assert.Equal(3, added![0].Quantity);
        Assert.Equal(ReservationStatus.Pending, added[0].Status);
        Assert.Equal(itemId, added[0].InventoryItemId);
    }

    [Fact]
    public async Task TC_INV_002_Handle_WhenAtomicCASFails_ReturnsOutOfStock()
    {
        var productId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        _reservations
            .Setup(r => r.GetReservationsForIdempotentReplayAsync(IdemK, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _items
            .Setup(r => r.GetPrimaryItemIdsByProductAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [productId] = itemId });

        _items
            .Setup(r => r.TryReserveAtomicAsync(itemId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _items
            .Setup(r => r.GetAvailableQuantityAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = CreateHandler();
        var cmd = new ReserveInventoryCommand(IdemK, [new ReserveInventoryLineItem(productId, 5)]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        var err = Assert.IsType<OutOfStockError>(result.Error);
        Assert.Equal(productId, err.ProductId);
        Assert.Equal(5, err.Requested);
        Assert.Equal(1, err.Available);
    }

    [Fact]
    public async Task TC_INV_003_Handle_WhenPendingExists_ReturnsSameReservationId()
    {
        var rid = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var expires = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        var existing = InventoryReservation.CreatePending(
            rid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            IdemSame,
            2,
            expires,
            DateTimeOffset.UtcNow);

        _reservations
            .Setup(r => r.GetReservationsForIdempotentReplayAsync(IdemSame, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var handler = CreateHandler();
        var cmd = new ReserveInventoryCommand(IdemSame, [new ReserveInventoryLineItem(existing.ProductId, 2)]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var payload = result.Value;
        Assert.NotNull(payload);
        Assert.Equal(rid, payload.ReservationId);
        Assert.Equal(expires, payload.ExpiresAt);
        _items.Verify(
            r => r.GetPrimaryItemIdsByProductAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _items.Verify(
            r => r.TryReserveAtomicAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(Skip = "P2-T2 TC-INV-004: needs PostgreSQL + concurrent load test (10 parallel vs 5 units on hand, atomic CAS).")]
    public void TC_INV_004_Concurrency_TenRequestsForFiveUnits_IntegrationOnly()
    {
    }

    [Fact]
    public async Task TC_INV_005_Handle_WhenProductMissing_Returns422Error()
    {
        var productId = Guid.NewGuid();

        _reservations
            .Setup(r => r.GetReservationsForIdempotentReplayAsync(IdemK, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _items
            .Setup(r => r.GetPrimaryItemIdsByProductAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = CreateHandler();
        var cmd = new ReserveInventoryCommand(IdemK, [new ReserveInventoryLineItem(productId, 1)]);

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        var err = Assert.IsType<ProductNotFoundForReservationError>(result.Error);
        Assert.Equal(productId, err.ProductId);
    }
}
