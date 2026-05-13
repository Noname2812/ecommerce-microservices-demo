namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

/// <summary>Redis keys for handler-level idempotency guard (defense-in-depth before quota burn).</summary>
internal static class PlaceSalesOrderIdempotencyCacheKeys
{
    /// <summary>Completed order id for a client idempotency key (value: order id string "D").</summary>
    public static string GuardKey(string idempotencyKey) =>
        $"sales-order:guard:{idempotencyKey}";
}
