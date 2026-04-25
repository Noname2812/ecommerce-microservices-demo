namespace UrbanX.Inventory.Domain.ValueObjects;

public static class MovementType
{
    public const string Receipt = "RECEIPT";
    public const string Sale = "SALE";
    public const string Return = "RETURN";
    public const string Adjustment = "ADJUSTMENT";
    public const string TransferIn = "TRANSFER_IN";
    public const string TransferOut = "TRANSFER_OUT";
    public const string Reservation = "RESERVATION";
    public const string Release = "RELEASE";
}
