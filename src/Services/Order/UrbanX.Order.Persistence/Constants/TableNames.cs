namespace UrbanX.Order.Persistence.Constants;

internal static class TableNames
{
    internal const string Orders = "orders";
    internal const string OrderItems = "order_items";
    internal const string OrderStatusHistories = "order_status_histories";
    internal const string PlaceSalesOrderSagas  = "place_sales_order_saga_states";
    internal const string PlaceOrderNormalSagas = "place_order_normal_saga_states";
    internal const string ProcessedEvents = "processed_events";
    internal const string CatalogSnapshots = "catalog_snapshots";

    internal const string ReadSchema = "read";
}
