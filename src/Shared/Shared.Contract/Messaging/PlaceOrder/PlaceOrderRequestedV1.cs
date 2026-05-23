using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Dtos.Payment;

namespace Shared.Contract.Messaging.PlaceOrder;

public record PlaceOrderRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required string UserId { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? CouponCode { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal ShippingFee { get; init; }
    public required IReadOnlyList<NormalOrderItemSnapshot> Items { get; init; }

    public OrderDtos.ShippingAddressSnapshot? ShippingAddress { get; init; }
    public string PricingSnapshotJson { get; init; } = "{}";
    public string CustomerEmail { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string? CustomerPhone { get; init; }
    public string? CustomerNote { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Sepay;
}

public record NormalOrderItemSnapshot(
    Guid ProductId,
    Guid VariantId,
    int Quantity,
    decimal UnitPrice);
