using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public class ConfirmReservationCommandHandlerTests
{
    private readonly Mock<IInventoryReservationRepository> _reservations = new();
    private readonly Mock<IInventoryItemRepository> _inventoryItems = new();
    private readonly Mock<IStockMovementRepository> _stockMovements = new();
    private readonly Mock<IProcessedEventRepository> _processedEvents = new();

    private ConfirmReservationCommandHandler CreateHandler() =>
        new(
            _reservations.Object,
            _inventoryItems.Object,
            _stockMovements.Object,
            _processedEvents.Object,
            NullLogger<ConfirmReservationCommandHandler>.Instance);

    [Fact]
    public async Task Handle_HappyPath_AtomicConfirmsAndDeductsAndAddsMovement()
    {
        var reservationId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var orderId = Guid.Parse("60000000-0000-4000-8000-000000000001");
        var itemId = Guid.Parse("70000000-0000-4000-8000-000000000001");
        var eventId = Guid.Parse("80000000-0000-4000-8000-000000000001");

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.TryMarkConfirmedAtomicAsync(reservationId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationConfirmResult(itemId, 5, orderId));

        _inventoryItems
            .Setup(r => r.ConfirmDeductAtomicAsync(itemId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

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

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.TryMarkConfirmedAtomicAsync(reservationId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationConfirmResult?)null);

        _reservations
            .Setup(r => r.GetStatusAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationStatus.Confirmed);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(reservationId, "idem:confirm-inv", eventId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _inventoryItems.Verify(
            r => r.ConfirmDeductAtomicAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stockMovements.Verify(
            r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _processedEvents.Verify(r => r.StageInsert(It.IsAny<ProcessedEvent>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEventAlreadyProcessed_ReturnsSuccessWithoutAtomicCalls()
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
            r => r.TryMarkConfirmedAtomicAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenReservationNotFound_ReturnsFailure()
    {
        var reservationId = Guid.NewGuid();

        _reservations
            .Setup(r => r.TryMarkConfirmedAtomicAsync(reservationId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationConfirmResult?)null);

        _reservations
            .Setup(r => r.GetStatusAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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

        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _reservations
            .Setup(r => r.TryMarkConfirmedAtomicAsync(reservationId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationConfirmResult?)null);

        _reservations
            .Setup(r => r.GetStatusAsync(reservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationStatus.Released);

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

    [Fact(Skip = "P2: PostgreSQL — two parallel atomic confirms for same reservation must deduct once (idempotency via atomic CAS).")]
    public void Handle_ConcurrentConfirm_IntegrationOnly()
    {
    }
}
