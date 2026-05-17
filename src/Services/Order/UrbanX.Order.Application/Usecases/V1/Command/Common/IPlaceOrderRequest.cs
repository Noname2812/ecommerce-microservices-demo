using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

public interface IPlaceOrderRequest
{
    PlaceOrderShippingAddressDto ShippingAddress { get; }
    decimal ShippingFee { get; }
    string? CouponCode { get; }
    string? CustomerNote { get; }
    string IdempotencyKey { get; }
    PlaceOrderPricingSnapshotDto PricingSnapshot { get; }
    IReadOnlyList<PlaceOrderLineDto> Items { get; }
    string? CustomerEmail { get; }
}
