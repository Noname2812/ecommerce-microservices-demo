using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrder;

public record PlaceOrderRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? CouponCode { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal ShippingFee { get; init; }
    public required IReadOnlyList<NormalOrderItemSnapshot> Items { get; init; }
}

public record NormalOrderItemSnapshot(
    Guid ProductId,
    Guid VariantId,
    int Quantity,
    decimal UnitPrice);
