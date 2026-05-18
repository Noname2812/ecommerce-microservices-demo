namespace Shared.Contract.Messaging.PlaceOrderSaga;

/// <summary>Inbox / contract marker for hard-deduct confirm messages from the order saga.</summary>
public interface IConfirmInventoryRequested
{
    Guid ReservationId { get; }
    string IdempotencyKey { get; }
}
