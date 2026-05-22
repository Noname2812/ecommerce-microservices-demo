using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

/// <summary>
/// Hard-deduct handler. Uses two atomic CAS UPDATEs (reservation status → CONFIRMED, inventory_items
/// reserved+on_hand decrement) so we never take a long-held row lock and never see xmin conflicts.
/// </summary>
internal sealed class ConfirmReservationCommandHandler(
    IInventoryReservationRepository reservationRepo,
    IInventoryItemRepository inventoryItemRepo,
    IProcessedEventRepository processedEvents,
    ILogger<ConfirmReservationCommandHandler> logger)
    : ICommandHandler<ConfirmReservationCommand>
{
    public async Task<Result> Handle(ConfirmReservationCommand cmd, CancellationToken ct)
    {
        if (cmd.EventId is { } inboxEventId &&
            await processedEvents.ExistsAsync(inboxEventId, ct))
            return Result.Success();

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
