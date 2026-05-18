using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

/// <summary>
/// Hard-deduct handler. Runs inside <see cref="Shared.Messaging.Behaviors.TransactionPipelineBehavior{TRequest,TResponse}"/>
/// with xmin retry on <see cref="InventoryItem"/>; concurrent redelivery is serialized via reservation row lock.
/// </summary>
internal sealed class ConfirmReservationCommandHandler(
    IInventoryReservationRepository reservationRepo,
    IStockMovementRepository stockMovementRepo,
    IProcessedEventRepository processedEvents,
    ILogger<ConfirmReservationCommandHandler> logger)
    : ICommandHandler<ConfirmReservationCommand>
{
    public async Task<Result> Handle(ConfirmReservationCommand cmd, CancellationToken ct)
    {
        if (cmd.EventId is { } inboxEventId &&
            await processedEvents.ExistsAsync(inboxEventId, ct))
            return Result.Success();

        var reservation = await reservationRepo.GetTrackedByIdWithInventoryItemForUpdateAsync(
            cmd.ReservationId,
            ct);

        if (reservation is null)
            return Result.Failure(InventoryReservationReleaseErrors.NotFound(cmd.ReservationId));

        if (reservation.Status == ReservationStatus.Confirmed)
        {
            logger.LogInformation(
                "Reservation {ReservationId} already confirmed; recording inbox event {EventId}",
                cmd.ReservationId, cmd.EventId);
            StageProcessedEventIfNeeded(cmd.EventId);
            return Result.Success();
        }

        if (reservation.Status != ReservationStatus.Pending)
            return Result.Failure(InventoryReservationReleaseErrors.NotConfirmable);

        var item = reservation.InventoryItem;
        if (item is null)
            return Result.Failure(InventoryReservationReleaseErrors.InventoryLineMissing);

        var utcNow = DateTimeOffset.UtcNow;
        var quantityOnHandBefore = item.QuantityOnHand;

        reservation.Confirm(utcNow);
        item.ConfirmDeduction(reservation.Quantity, utcNow);

        var movement = StockMovement.CreateSale(
            inventoryItemId: item.Id,
            quantity: reservation.Quantity,
            referenceType: StockMovementReferenceType.Order,
            referenceId: reservation.OrderId,
            note: ConfirmReservationAudit.MovementNote,
            createdById: null,
            createdByName: ConfirmReservationAudit.CreatedByName,
            utcNow: utcNow,
            quantityOnHandBefore: quantityOnHandBefore);

        await stockMovementRepo.AddAsync(movement, ct);

        StageProcessedEventIfNeeded(cmd.EventId);

        return Result.Success();
    }

    private void StageProcessedEventIfNeeded(Guid? eventId)
    {
        if (eventId is null)
            return;

        processedEvents.StageInsert(
            new ProcessedEvent
            {
                EventId = eventId.Value,
                EventType = nameof(IConfirmInventoryRequested),
                ProcessedAt = DateTimeOffset.UtcNow
            });
    }
}
