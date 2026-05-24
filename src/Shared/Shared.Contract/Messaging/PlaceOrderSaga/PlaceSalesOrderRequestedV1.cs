using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Order;
using Shared.Contract.Dtos.Payment;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

public record PlaceSalesOrderRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required Guid CampaignId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required decimal ExpectedTotal { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal ShippingFee { get; init; }
    public required OrderDtos.ShippingAddressSnapshot ShippingAddress { get; init; }
    public string? CouponCode { get; init; }
    public required IReadOnlyList<OrderItemSnapshot> Items { get; init; }
    public required PricingSnapshot PricingSnapshot { get; init; }
    public string CustomerEmail { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string? CustomerPhone { get; init; }
    public string? CustomerNote { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Sepay;
}

public record OrderItemSnapshot(
    Guid ProductId,
    Guid VariantId,
    int Quantity,
    decimal UnitPrice,
    int Version);

public record PricingSnapshot(
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalBeforeDiscount);
