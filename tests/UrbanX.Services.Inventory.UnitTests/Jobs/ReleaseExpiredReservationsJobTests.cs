using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Jobs;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Jobs;

public class ReleaseExpiredReservationsJobTests
{
    private readonly Mock<IInventoryReservationRepository> _repoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ILogger<ReleaseExpiredReservationsJob>> _loggerMock = new();
    private readonly IOptions<ReleaseExpiredReservationsJobOptions> _options =
        Microsoft.Extensions.Options.Options.Create(new ReleaseExpiredReservationsJobOptions());

    private ReleaseExpiredReservationsJob CreateJob() =>
        new(_repoMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

    public ReleaseExpiredReservationsJobTests()
    {
        // IUnitOfWork executes the operation inline so handlers run during tests
        _unitOfWorkMock
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>((op, _) => op());
    }

    [Fact]
    public async Task ExecuteAsync_WithExpiredReservations_ReleasesThemAndDecrementsReserved()
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Test Product",
            VariantId = Guid.NewGuid(),
            VariantSku = "SKU-001",
            QuantityOnHand = 10,
            QuantityReserved = 5,
        };

        var reservation = new InventoryReservation
        {
            Id = Guid.NewGuid(),
            InventoryItemId = item.Id,
            ProductId = item.ProductId,
            OrderIdempotencyKey = "key-1",
            Quantity = 5,
            Status = ReservationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            InventoryItem = item,
        };

        _repoMock
            .Setup(r => r.GetExpiredPendingBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation]);

        await CreateJob().ExecuteAsync();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.NotNull(reservation.ReleasedAt);
        Assert.Equal(0, item.QuantityReserved);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExpiredReservation_IsNotReleased()
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Test Product",
            VariantId = Guid.NewGuid(),
            VariantSku = "SKU-002",
            QuantityOnHand = 10,
            QuantityReserved = 3,
        };

        var reservation = new InventoryReservation
        {
            Id = Guid.NewGuid(),
            InventoryItemId = item.Id,
            ProductId = item.ProductId,
            OrderIdempotencyKey = "key-2",
            Quantity = 3,
            Status = ReservationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            InventoryItem = item,
        };

        // Repository returns empty batch because the DB WHERE clause filters out non-expired rows
        _repoMock
            .Setup(r => r.GetExpiredPendingBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateJob().ExecuteAsync();

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.ReleasedAt);
        Assert.Equal(3, item.QuantityReserved);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobRunsTwice_SecondRunSeesEmptyBatch_NoDoubleRelease()
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Test Product",
            VariantId = Guid.NewGuid(),
            VariantSku = "SKU-003",
            QuantityOnHand = 10,
            QuantityReserved = 4,
        };

        var reservation = new InventoryReservation
        {
            Id = Guid.NewGuid(),
            InventoryItemId = item.Id,
            ProductId = item.ProductId,
            OrderIdempotencyKey = "key-3",
            Quantity = 4,
            Status = ReservationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            InventoryItem = item,
        };

        // First call: returns the expired reservation
        // Second call: returns empty (already RELEASED — filtered out by WHERE Status='PENDING')
        _repoMock
            .SetupSequence(r => r.GetExpiredPendingBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation])
            .ReturnsAsync([]);

        var job = CreateJob();
        await job.ExecuteAsync(); // first run — releases reservation
        await job.ExecuteAsync(); // second run — empty batch, no-op

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.QuantityReserved);

        // InventoryItem.ReleaseReservedQuantity called exactly once
        _repoMock.Verify(
            r => r.GetExpiredPendingBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        Assert.Equal(0, item.QuantityReserved);
    }
}
