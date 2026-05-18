using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record ReserveInventoryRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string OrderIdempotencyKey { get; init; }
    public required IReadOnlyList<InventoryReserveItem> Items { get; init; }
}

public record InventoryReservedV1 : IntegrationEventBase
{
    public override string Source => "inventory-service";

    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required IReadOnlyList<InventoryReserveItem> Items { get; init; }
}

public record InventoryReserveFailedV1 : IntegrationEventBase
{
    public override string Source => "inventory-service";

    public required Guid OrderId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required IReadOnlyList<OutOfStockProduct> OutOfStockProducts { get; init; }
}

public record InventoryReserveItem(Guid ProductId, Guid VariantId, int Quantity);

public record OutOfStockProduct(Guid ProductId, int Available);

public record ConfirmInventoryRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
    public required string IdempotencyKey { get; init; }
}
