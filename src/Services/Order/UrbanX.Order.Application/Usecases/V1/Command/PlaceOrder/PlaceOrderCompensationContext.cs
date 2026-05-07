namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCompensationContext
{
    public Guid? ReservationId { get; internal set; }
    public Guid? CouponClaimId { get; internal set; }
}
