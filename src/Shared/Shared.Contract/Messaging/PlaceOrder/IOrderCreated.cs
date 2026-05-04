namespace Shared.Contract.Messaging.PlaceOrder;

public interface IOrderCreated : IPlaceOrderIntegrationContract
{
    Guid OrderId { get; }
    Guid UserId { get; }
    IReadOnlyList<IOrderCreatedItem> Items { get; }
    decimal TotalAmount { get; }
    string? CouponCode { get; }
    string IdempotencyKey { get; }
    DateTimeOffset CreatedAt { get; }
}

public interface IOrderCreatedItem
{
    Guid ProductId { get; }
    int Quantity { get; }
}
