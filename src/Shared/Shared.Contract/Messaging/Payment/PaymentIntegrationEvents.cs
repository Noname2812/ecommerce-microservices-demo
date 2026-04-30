using Shared.Contract.Abstractions;

namespace Shared.Contract.Messaging.Payment;

public static class PaymentIntegrationEvents
{
    public record PaymentCompletedV1(
        Guid PaymentId,
        Guid OrderId,
        string OrderNumber,
        Guid CustomerId,
        decimal Amount,
        string Currency,
        string ProviderName,
        string? ProviderTransactionId,
        DateTimeOffset PaidAt
    ) : IntegrationEventBase
    {
        public override string Source => "payment-service";
    }

    public record PaymentFailedV1(
        Guid PaymentId,
        Guid OrderId,
        string OrderNumber,
        Guid CustomerId,
        string FailureReason
    ) : IntegrationEventBase
    {
        public override string Source => "payment-service";
    }

    public record RefundProcessedV1(
        Guid RefundId,
        Guid PaymentId,
        Guid OrderId,
        decimal RefundAmount,
        string Currency,
        string? ProviderRefundId,
        DateTimeOffset ProcessedAt
    ) : IntegrationEventBase
    {
        public override string Source => "payment-service";
    }
}
