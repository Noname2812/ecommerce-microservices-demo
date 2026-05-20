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
/// Hard-deduct handler. Uses two atomic CAS UPDATEs (reservation status → CONFIRMED, inventory_items
/// reserved+on_hand decrement) so we never take a long-held row lock and never see xmin conflicts.
/// </summary>
internal sealed class ConfirmReservationCommandHandler(
    IInventoryReservationRepository reservationRepo,
    IInventoryItemRepository inventoryItemRepo,
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

        var utcNow = DateTimeOffset.UtcNow;

        // Step 1: Atomic transition PENDING → CONFIRMED. RETURNING gives us item id + quantity in one round-trip.
        var changed = await reservationRepo.TryMarkConfirmedAtomicAsync(cmd.ReservationId, utcNow, ct);

        if (changed is null)
        {
            // No row updated → either not found or status ≠ PENDING. Distinguish via a status read.
            var status = await reservationRepo.GetStatusAsync(cmd.ReservationId, ct);
            switch (status)
            {
                case null:
                    return Result.Failure(InventoryReservationReleaseErrors.NotFound(cmd.ReservationId));

                case ReservationStatus.Confirmed:
                    logger.LogInformation(
                        "Reservation {ReservationId} already confirmed; recording inbox event {EventId}",
                        cmd.ReservationId, cmd.EventId);
                    StageProcessedEventIfNeeded(cmd.EventId);
                    return Result.Success();

                default:
                    return Result.Failure(InventoryReservationReleaseErrors.NotConfirmable);
            }
        }

        // Step 2: Hard-deduct quantity_reserved + quantity_on_hand atomically; RETURNING the pre-update
        // quantity_on_hand for the stock_movement audit row.
        var quantityOnHandBefore = await inventoryItemRepo.ConfirmDeductAtomicAsync(
            changed.InventoryItemId, changed.Quantity, utcNow, ct);

        if (quantityOnHandBefore is null)
        {
            // Invariant violation: reservation said PENDING + quantity, but inventory_items.quantity_reserved
            // < quantity. CHECK constraint or earlier release path is the only way this can happen.
            // Surface a failure so the transaction rolls back and the consumer DLQs for investigation.
            return Result.Failure(InventoryReservationReleaseErrors.InventoryLineMissing);
        }

        var movement = StockMovement.CreateSale(
            inventoryItemId: changed.InventoryItemId,
            quantity: changed.Quantity,
            referenceType: StockMovementReferenceType.Order,
            referenceId: changed.OrderId,
            note: ConfirmReservationAudit.MovementNote,
            createdById: null,
            createdByName: ConfirmReservationAudit.CreatedByName,
            utcNow: utcNow,
            quantityOnHandBefore: quantityOnHandBefore.Value);

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
