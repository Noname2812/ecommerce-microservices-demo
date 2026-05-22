using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record InventoryReserveItem(Guid VariantId, int Quantity);

public record ReserveInventoryRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required IReadOnlyList<InventoryReserveItem> Items { get; init; }
    public double ExpiresInMinutes { get; init; }
}

public record InventoryReservedV1 : IntegrationEventBase
{
    public override string Source => "inventory-service";

    public required Guid OrderId { get; init; }
    public required List<Guid> ReservationIds { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public record InventoryReserveFailedV1 : IntegrationEventBase
{
    public override string Source => "inventory-service";

    public required Guid OrderId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required IReadOnlyList<Guid> VariantIdsOutOfStock { get; init; }
}


public record ConfirmInventoryRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
}
