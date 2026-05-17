using Shared.Messaging.Saga;

namespace UrbanX.Order.Application.Sagas;

public sealed class PlaceOrderNormalSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    public Guid OrderId { get; set; }
    public string UserId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;
    public string? CouponCode { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal PromotionDiscount { get; set; }
    public decimal CouponDiscount { get; set; }

    // JSON: List<NormalOrderItemSnapshot>
    public string? ItemsJson { get; set; }

    // Side-effect tracking (compensation)
    public Guid? ReservationId { get; set; }
    public Guid? CouponClaimId { get; set; }

    // Payment
    public string? PaymentSessionId { get; set; }

    // Scheduled timeout tokens
    public Guid? StepTimeoutTokenId { get; set; }
    public Guid? PaymentExpiryTokenId { get; set; }

    // Failure info
    public string? FailureStep { get; set; }
    public string? FailureReason { get; set; }
}
