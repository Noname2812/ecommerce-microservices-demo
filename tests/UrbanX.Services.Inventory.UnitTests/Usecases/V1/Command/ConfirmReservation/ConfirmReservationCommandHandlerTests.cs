using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public class ConfirmReservationCommandHandlerTests
{
    private readonly Mock<IInventoryReservationRepository> _reservations = new();
    private readonly Mock<IStockMovementRepository> _stockMovements = new();
    private readonly Mock<IProcessedEventRepository> _processedEvents = new();

    private ConfirmReservationCommandHandler CreateHandler() =>
        new(
            _reservations.Object,
            _stockMovements.Object,
            _processedEvents.Object,
            NullLogger<ConfirmReservationCommandHandler>.Instance);

    [Fact]
    public async Task Handle_HappyPath_ConfirmsReservationDeductsStockAndAddsMovement()
    {
        var reservationId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var orderId = Guid.Parse("60000000-0000-4000-8000-000000000001");
        var eventId = Guid.Parse("80000000-0000-4000-8000-000000000001");
        var item = new InventoryItem
        {
            Id = Guid.Parse("70000000-0000-4000-8000-000000000001"),
            QuantityOnHand = 50,
            QuantityReserved = 5
        };
        var reservation = new InventoryReservation
        {
            Id = reservationId,
            OrderId = orderId,
            Quantity = 5,
            Status = ReservationStatus.Pending,
            InventoryItem = item
        };

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.GetTrackedByIdWithInventoryItemForUpdateAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        StockMovement? movement = null;
        _stockMovements
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => movement = m)
            .Returns(Task.CompletedTask);

        ProcessedEvent? staged = null;
        _processedEvents
            .Setup(r => r.StageInsert(It.IsAny<ProcessedEvent>()))
            .Callback<ProcessedEvent>(e => staged = e);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(reservationId, "idem:confirm-inv", eventId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.NotNull(reservation.ConfirmedAt);
        Assert.Equal(0, item.QuantityReserved);
        Assert.Equal(45, item.QuantityOnHand);
        Assert.NotNull(movement);
        Assert.Equal(MovementType.Sale, movement!.MovementType);
        Assert.Equal(-5, movement.QuantityChange);
        Assert.Equal(50, movement.QuantityBefore);
        Assert.Equal(45, movement.QuantityAfter);
        Assert.Equal(StockMovementReferenceType.Order, movement.ReferenceType);
        Assert.Equal(orderId, movement.ReferenceId);
        Assert.Equal(ConfirmReservationAudit.MovementNote, movement.Note);
        Assert.Null(movement.CreatedById);
        Assert.Equal(ConfirmReservationAudit.CreatedByName, movement.CreatedByName);
        Assert.NotNull(staged);
        Assert.Equal(eventId, staged!.EventId);
        Assert.Equal(nameof(Shared.Contract.Messaging.PlaceOrderSaga.IConfirmInventoryRequested), staged.EventType);
    }

    [Fact]
    public async Task Handle_WhenAlreadyConfirmed_ReturnsSuccessWithoutMovementAndStagesInbox()
    {
        var reservationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var reservation = new InventoryReservation
        {
            Id = reservationId,
            Status = ReservationStatus.Pending,
            Quantity = 2,
            InventoryItem = new InventoryItem { QuantityOnHand = 10, QuantityReserved = 2 }
        };
        reservation.Confirm(DateTimeOffset.UtcNow);

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.GetTrackedByIdWithInventoryItemForUpdateAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(reservationId, "idem:confirm-inv", eventId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _stockMovements.Verify(
            r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _processedEvents.Verify(r => r.StageInsert(It.IsAny<ProcessedEvent>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEventAlreadyProcessed_ReturnsSuccessWithoutLoadingReservation()
    {
        var eventId = Guid.NewGuid();

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(Guid.NewGuid(), "idem:confirm-inv", eventId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _reservations.Verify(
            r => r.GetTrackedByIdWithInventoryItemForUpdateAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenReservationNotFound_ReturnsFailure()
    {
        var reservationId = Guid.NewGuid();

        _reservations
            .Setup(r => r.GetTrackedByIdWithInventoryItemForUpdateAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryReservation?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(reservationId, "idem:confirm-inv"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InventoryReservation.NotFound", result.Error.Code);
        _processedEvents.Verify(r => r.StageInsert(It.IsAny<ProcessedEvent>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNotConfirmable_ReturnsFailureAndDoesNotStageInbox()
    {
        var reservationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var reservation = new InventoryReservation
        {
            Id = reservationId,
            Status = ReservationStatus.Released,
            Quantity = 3,
            InventoryItem = new InventoryItem { QuantityOnHand = 10, QuantityReserved = 0 }
        };

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.GetTrackedByIdWithInventoryItemForUpdateAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(reservationId, "idem:confirm-inv", eventId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InventoryReservation.NotConfirmable", result.Error.Code);
        _stockMovements.Verify(
            r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _processedEvents.Verify(r => r.StageInsert(It.IsAny<ProcessedEvent>()), Times.Never);
    }

    [Fact(Skip = "P2: PostgreSQL + xmin — two parallel confirms for same reservation must deduct once (see TC_INV_004).")]
    public void Handle_ConcurrentConfirm_IntegrationOnly()
    {
    }
}
