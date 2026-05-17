using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Payment;

public record PaymentSessionCreatedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required string PaymentSessionId { get; init; }
    public required string PaymentUrl { get; init; }
    public string? QrCodeUrl { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
