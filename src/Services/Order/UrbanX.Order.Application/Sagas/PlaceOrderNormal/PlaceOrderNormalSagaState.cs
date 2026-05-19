using Shared.Messaging.Saga;

namespace UrbanX.Order.Application.Sagas.PlaceOrderNormal;

public sealed class PlaceOrderNormalSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    public Guid OrderId { get; set; }
    public required string OrderNumber { get; set; }
    public string UserId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;
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
    public Guid? ReservationId { get; set; }
    public Guid? CouponClaimId { get; set; }

    // Catalog validation cache
    public string? VariantsJson { get; set; }       // List<CatalogVariantInfo> — set after ValidateThroughCatalog
    public string? ValidationError { get; set; }

    // Payment
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
