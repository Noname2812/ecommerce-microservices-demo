namespace Shared.Contract.Messaging.PlaceOrder;

public interface IOrderConfirmed : IPlaceOrderIntegrationContract
{
    Guid OrderId { get; }
    Guid UserId { get; }
    Guid ReservationId { get; }
    Guid? ClaimId { get; }
    decimal FinalAmount { get; }
    DateTimeOffset ConfirmedAt { get; }
}
