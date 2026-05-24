using Shared.Contract.Dtos.Payment;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

public interface IPlaceOrderRequest
{
    PlaceOrderShippingAddressDto ShippingAddress { get; }
    decimal ShippingFee { get; }

    /// <summary>
    /// Legacy direct-code path (still used by PlaceSalesOrder which acquires a Redis lock at saga time).
    /// New Normal orders use <see cref="CouponHoldToken"/> instead.
    /// </summary>
    string? CouponCode { get; }

    /// <summary>
    /// Cart-issued hold token from <c>POST /api/v1/promotion/coupon-holds</c>. Validation, user-lock,
    /// and quota reservation already happened at hold-time. The Normal saga resolves this to a coupon
    /// snapshot via cross-service Redis read — no Promotion call on the order critical path.
    /// </summary>
    string? CouponHoldToken { get; }

    string? CustomerNote { get; }
    string IdempotencyKey { get; }
    PlaceOrderPricingSnapshotDto PricingSnapshot { get; }
    IReadOnlyList<PlaceOrderLineDto> Items { get; }
    string? CustomerEmail { get; }
    PaymentMethod PaymentMethod { get; }
}
