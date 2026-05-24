using Shared.Contract.Dtos.Payment;
using Shared.Messaging.Saga;

namespace UrbanX.Order.Application.Sagas.PlaceOrderNormal;

public sealed class PlaceOrderNormalSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    public Guid OrderId { get; set; }
    public required string OrderNumber { get; set; }
    public string UserId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;

    /// <summary>Set on the inbound event; resolved at saga start.</summary>
    public string? CouponHoldToken { get; set; }

    /// <summary>Populated after the hold token resolves to a coupon — used by post-payment claim publish.</summary>
    public string? CouponCode { get; set; }

    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal CouponDiscount { get; set; }

    // JSON: List<NormalOrderItemSnapshot>
    public string? ItemsJson { get; set; }

    public string? ShippingAddressJson { get; set; }
    public string PricingSnapshotJson { get; set; } = "{}";
    public string CustomerEmail { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerPhone { get; set; }
    public string? CustomerNote { get; set; }

    // Side-effect tracking (compensation)
    public List<Guid> ReservationIds { get; set; } = new();
    public Guid? CouponClaimId { get; set; }

    // Catalog validation cache
    public string? VariantsJson { get; set; }       // List<CatalogVariantInfo> — set after ValidateThroughCatalog
    public string? ValidationError { get; set; }

    // Payment
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Sepay;
    public string? PaymentSessionId { get; set; }
    public string? PaymentUrl { get; set; }
    public string? QrCodeUrl { get; set; }
    public DateTimeOffset? PaymentExpiresAt { get; set; }

    // Scheduled timeout tokens
    public Guid? PaymentExpiryTokenId { get; set; }
    public Guid? InventoryExpiryTokenId { get; set; }
    public Guid? CouponExpiryTokenId { get; set; }
    public Guid? PaymentSessionExpiryTokenId { get; set; }

    // Failure info
    public string? FailureStep { get; set; }
    public string? FailureReason { get; set; }
}
