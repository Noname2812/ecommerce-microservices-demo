using Shared.Kernel.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Domain.Models;

public class PaymentEvent : BaseEntity<Guid>
{
    public Guid PaymentId { get; set; }
    public string EventType { get; set; } = null!;
    public string? Payload { get; set; }
    public string Source { get; set; } = EventSource.Internal;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Payment? Payment { get; set; }
}
