using Shared.Kernel.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Domain.Models;

public class Payment : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public string CustomerEmail { get; set; } = null!;

    public Guid ProviderId { get; set; }
    public string ProviderName { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";

    public string? ProviderTransactionId { get; set; }
    public string? ProviderResponse { get; set; }
    public string Status { get; set; } = PaymentStatus.Pending;
    public string? FailureReason { get; set; }

    public string IdempotencyKey { get; set; } = null!;
    public string? PaymentMethodDetails { get; set; }
    public string? IpAddress { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PaymentProvider? Provider { get; set; }
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
    public ICollection<PaymentEvent> Events { get; set; } = new List<PaymentEvent>();

    public void MarkProcessing()
    {
        Status = PaymentStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted(string? providerTransactionId)
    {
        Status = PaymentStatus.Completed;
        ProviderTransactionId = providerTransactionId;
        PaidAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
