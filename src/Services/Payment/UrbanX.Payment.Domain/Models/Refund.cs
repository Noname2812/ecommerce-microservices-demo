using Shared.Kernel.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Domain.Models;

public class Refund : BaseEntity<Guid>
{
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string? ProviderRefundId { get; set; }
    public string Status { get; set; } = RefundStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Payment? Payment { get; set; }

    public void MarkCompleted(string? providerRefundId)
    {
        Status = RefundStatus.Completed;
        ProviderRefundId = providerRefundId;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
    }
}
