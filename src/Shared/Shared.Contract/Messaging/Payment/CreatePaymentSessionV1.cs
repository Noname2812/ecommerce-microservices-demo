using Shared.Contract.Abstractions;
using Shared.Contract.Dtos.Payment;

namespace Shared.Contract.Messaging.Payment;

public record CreatePaymentSessionV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public string? OrderNumber { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CustomerEmail { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Sepay;
}
