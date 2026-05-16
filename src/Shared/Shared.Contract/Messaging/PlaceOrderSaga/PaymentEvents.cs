using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.PlaceOrderSaga;

/// <summary>Saga publishes this to trigger payment processing by the Payment service.</summary>
public record ProcessPaymentRequestedV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    /// <summary>{IdempotencyKey}:pay — allows payment service to deduplicate retries.</summary>
    public required string OrderIdempotencyKey { get; init; }
    public required decimal FinalAmount { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid ReservationId { get; init; }
    public Guid? CouponClaimId { get; init; }
}

/// <summary>Payment service publishes when the payment charge succeeds.</summary>
public record PaymentProcessedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>Payment service publishes when the payment charge fails (declined, timeout, etc.).</summary>
public record PaymentProcessFailedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required string ErrorMessage { get; init; }
    public string? FailureCode { get; init; }
}
