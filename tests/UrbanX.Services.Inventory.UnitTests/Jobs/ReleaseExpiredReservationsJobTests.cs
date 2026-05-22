using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;
using UrbanX.Inventory.Infrastructure.DependencyInjection.Options;
using UrbanX.Inventory.Infrastructure.Jobs;

namespace UrbanX.Services.Inventory.UnitTests.Jobs;

public sealed class ReleaseExpiredReservationsJobTests
{
    private readonly Mock<IInventoryReservationRepository> _repoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ILogger<ReleaseExpiredReservationsJob>> _loggerMock = new();
    private readonly IOptions<ReleaseExpiredReservationsJobOptions> _options =
        Options.Create(new ReleaseExpiredReservationsJobOptions());

    public ReleaseExpiredReservationsJobTests()
    {
        _unitOfWorkMock
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>((op, _) => op());
    }

    private ReleaseExpiredReservationsJob CreateJob() =>
        new(_repoMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

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
            IconUrl = "https://example.com/icon.png",
            QuantityOnHand = 10,
            QuantityReserved = 5,
        };

        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            item.Id,
            5,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow);
        reservation.InventoryItem = item;

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
            IconUrl = "https://example.com/icon.png",
            QuantityOnHand = 10,
            QuantityReserved = 3,
        };

        var reservation = InventoryReservation.CreatePending(
            Guid.NewGuid(),
            item.Id,
            3,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow);
        reservation.InventoryItem = item;

        _repoMock
            .Setup(r => r.GetExpiredPendingBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateJob().ExecuteAsync();

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.ReleasedAt);
        Assert.Equal(3, item.QuantityReserved);
    }
}
