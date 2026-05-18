using Shared.Messaging.Saga;

namespace UrbanX.Order.Application.Sagas;

// Placed in Application (not Domain) because SagaStateBase depends on Shared.Messaging/MassTransit.
public sealed class PlaceSalesOrderSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    // ── Order identity ────────────────────────────────────────────────────────
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = default!;
    public Guid CampaignId { get; set; }
    public string IdempotencyKey { get; set; } = default!;

    // ── Pricing snapshot ──────────────────────────────────────────────────────
    public decimal Subtotal { get; set; }
    public decimal ExpectedTotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal PromotionDiscount { get; set; }
    public decimal CouponDiscount { get; set; }

    // ── Runtime data serialised for downstream steps ──────────────────────────
    // JSON: List<Shared.Contract.Messaging.PlaceOrderSaga.OrderItemSnapshot>
    public string? ItemsJson { get; set; }
    public string? CouponCode { get; set; }

    // ── Side-effect tracking (used for compensation rollback) ─────────────────
    public Guid? ReservationId { get; set; }
    public Guid? CouponClaimId { get; set; }
    // JSON: List<Shared.Contract.Messaging.PlaceOrderSaga.ClaimedFlashSaleSlot>
    public string? ClaimedFlashSaleSlotsJson { get; set; }
    public bool QuotaReserved { get; set; }
    public int QuotaQuantity { get; set; }

    // ── Payment tracking ──────────────────────────────────────────────────────
    public Guid? PaymentId { get; set; }
    public string? PaymentSessionId { get; set; }

    // ── Failure info ──────────────────────────────────────────────────────────
    public string? FailureStep { get; set; }
    public string? FailureReason { get; set; }

    // ── Scheduled timeout tokens ──────────────────────────────────────────────
    public Guid? TimeoutTokenId { get; set; }
    public Guid? PaymentExpiryTokenId { get; set; }
}
