using Shared.Messaging.Saga;

namespace UrbanX.Order.Application.Sagas;

// Placed in Application (not Domain) because SagaStateBase depends on Shared.Messaging/MassTransit.
public sealed class PlaceSalesOrderSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    // ── Order identity ────────────────────────────────────────────────────────
    public Guid OrderId { get; set; }
    /// <summary>
    /// Always a valid Guid in canonical "D" format after <c>SnapshotRequest</c> — that step
    /// throws if the inbound event carries a malformed value, so downstream saga code can
    /// parse without falling back to silent skips.
    /// </summary>
    public string UserId { get; set; } = default!;
    public Guid CampaignId { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    public string? CouponCode { get; set; }

    // ── Pricing snapshot (server-side authoritative) ──────────────────────────
    public decimal ExpectedTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal SaleDiscount { get; set; }
    public decimal CouponDiscount { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal FinalTotal { get; set; }

    public DateTimeOffset? SaleStartAt { get; set; }
    public DateTimeOffset? SaleEndAt { get; set; }

    // ── Runtime data serialised for downstream steps ──────────────────────────
    // JSON: List<Shared.Contract.Messaging.PlaceOrderSaga.OrderItemSnapshot>
    public string? ItemsJson { get; set; }
    // JSON: List<CatalogVariantInfo> — set after ValidateThroughCatalog
    public string? VariantsJson { get; set; }
    public string? ShippingAddressJson { get; set; }

    // ── Customer info ─────────────────────────────────────────────────────────
    public string CustomerEmail { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerPhone { get; set; }
    public string? CustomerNote { get; set; }

    // ── Side-effect tracking (used for compensation rollback) ─────────────────
    /// <summary>True once <c>CreateSalesOrderAsync</c> has persisted the Order row.</summary>
    public bool OrderPersisted { get; set; }
    public Guid? ReservationId { get; set; }
    public bool CouponLocked { get; set; }

    // ── Payment tracking ──────────────────────────────────────────────────────
    public string? PaymentSessionId { get; set; }
    public string? PaymentUrl { get; set; }
    public string? QrCodeUrl { get; set; }
    public DateTimeOffset? PaymentExpiresAt { get; set; }

    // ── Failure info ──────────────────────────────────────────────────────────
    public string? ValidationError { get; set; }
    public string? FailureStep { get; set; }
    public string? FailureReason { get; set; }

    // ── Scheduled timeout tokens ──────────────────────────────────────────────
    public Guid? StepTimeoutTokenId { get; set; }
    public Guid? PaymentExpiryTokenId { get; set; }
}
