using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Domain.Errors;

public static class InventoryReservationReleaseErrors
{
    public static Error NotFound(Guid reservationId) =>
        new("InventoryReservation.NotFound", $"Reservation {reservationId} was not found.");

    public static readonly Error NotReleasable =
        new("InventoryReservation.NotReleasable", "Reservation cannot be released in its current status.");

    public static readonly Error InventoryLineMissing =
        new("InventoryReservation.InventoryLineMissing", "Inventory line for this reservation is missing.");

    public static readonly Error NotConfirmable =
        new("InventoryReservation.NotConfirmable", "Reservation cannot be confirmed in its current status.");
}
