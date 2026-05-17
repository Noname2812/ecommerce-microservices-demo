using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Order;

public static class OrderIntegrationEvents
{
    public record OrderCreatedV1(
        Guid OrderId,
        string OrderNumber,
        Guid CustomerId,
        string CustomerEmail,
        string CustomerName,
        decimal TotalAmount,
        IReadOnlyList<OrderItemSnapshot> Items
    ) : IntegrationEventBase
    {
        public override string Source => "order-service";
    }

    public record OrderCancelledV1(
        Guid OrderId,
        string OrderNumber,
        string Reason
    ) : IntegrationEventBase
    {
        public override string Source => "order-service";
    }
}

public record OrderItemSnapshot(
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string VariantSku,
    string? VariantName,
    Guid SellerId,
    string SellerName,
    int Quantity,
    decimal UnitPrice
);
