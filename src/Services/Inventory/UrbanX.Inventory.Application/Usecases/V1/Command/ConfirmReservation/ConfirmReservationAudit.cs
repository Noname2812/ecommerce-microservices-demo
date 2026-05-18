namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

internal static class ConfirmReservationAudit
{
    public const string CreatedByName = "system:saga-confirm";
    public const string MovementNote = "ORDER_CONFIRMED";
}
