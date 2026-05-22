using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

internal sealed class ConfirmReservationCommandHandler(
    IInventoryReservationRepository reservationRepository,
    IInventoryItemRepository inventoryItemRepository) : ICommandHandler<ConfirmReservationCommand>
{
    public async Task<Result> Handle(ConfirmReservationCommand request, CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var confirmed = await reservationRepository.TryMarkConfirmedByOrderIdAsync(
            request.OrderId,
            utcNow,
            cancellationToken);

        if (confirmed.Count == 0)
            return Result.Success();

        foreach (var row in confirmed)
        {
            await inventoryItemRepository.ConfirmDeductAtomicAsync(
                row.InventoryItemId,
                row.Quantity,
                utcNow,
                cancellationToken);
        }

        return Result.Success();
    }
}
