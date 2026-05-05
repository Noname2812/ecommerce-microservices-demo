using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Application.Usecases.V1.Errors;

public static class InventoryReservationReleaseErrors
{
    public static Error NotFound(Guid reservationId) =>
        new("InventoryReservation.NotFound", $"Reservation {reservationId} was not found.");

    public static readonly Error NotReleasable =
        new("InventoryReservation.NotReleasable", "Reservation cannot be released in its current status.");

    public static readonly Error InventoryLineMissing =
        new("InventoryReservation.InventoryLineMissing", "Inventory line for this reservation is missing.");
}
