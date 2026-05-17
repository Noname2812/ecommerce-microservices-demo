using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Payment;

/// <summary>Published by Payment service when user completes a checkout session (URL/QR payment).</summary>
public record PaymentSessionCompletedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required string PaymentSessionId { get; init; }
    public required decimal AmountPaid { get; init; }
    public required DateTimeOffset PaidAt { get; init; }
}
