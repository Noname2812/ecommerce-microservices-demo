namespace Shared.Outbox
{
    /// <summary>
    /// Represents a message stored in the outbox table.
    /// </summary>
    public sealed class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EventType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ProcessedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
        public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    }

    public enum OutboxMessageStatus
    {
        Pending,
        Processing,
        Processed,
        Failed
    }
}
