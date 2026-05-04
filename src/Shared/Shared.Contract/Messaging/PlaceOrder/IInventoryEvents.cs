namespace Shared.Contract.Messaging.PlaceOrder;

public interface IInventoryReserved : IPlaceOrderIntegrationContract
{
    Guid ReservationId { get; }
    string OrderIdempotencyKey { get; }
    IReadOnlyList<IInventoryReservedItem> Items { get; }
    DateTimeOffset ExpiresAt { get; }
}

/// <summary>
/// <see cref="VariantId"/> extends the minimal P1-T2 line item for UrbanX (catalog variants).
/// Confirm with consumers before changing — affects inventory projections.
/// </summary>
public interface IInventoryReservedItem
{
    Guid ProductId { get; }
    Guid VariantId { get; }
    int Quantity { get; }
}

public interface IInventoryReserveFailed : IPlaceOrderIntegrationContract
{
    string OrderIdempotencyKey { get; }
    string Reason { get; }
    Guid? FailedProductId { get; }
}

public interface IInventoryReleaseRequested : IPlaceOrderIntegrationContract
{
    Guid ReservationId { get; }
    string Reason { get; }
    string CorrelationId { get; }
}
