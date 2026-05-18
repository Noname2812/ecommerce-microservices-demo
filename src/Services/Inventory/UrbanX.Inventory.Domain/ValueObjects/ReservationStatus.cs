namespace UrbanX.Inventory.Domain.ValueObjects;

public static class ReservationStatus
{
    /// <summary>Stock held for an in-flight order (place-order idempotency checks this status).</summary>
    public const string Pending = "PENDING";

    public const string Confirmed = "CONFIRMED";
    public const string Released = "RELEASED";
    public const string Cancelled = "CANCELLED";
}
