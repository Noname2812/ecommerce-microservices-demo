namespace Shared.Outbox
{
    /// <summary>
    /// Represents a message stored in the outbox table (transactional outbox).
    /// DB columns: EventType, Error (see <see cref="EfCore.OutboxMessageEntityTypeConfiguration"/>).
    /// </summary>
    public sealed class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Message type (assembly-qualified name for integration events, or caller-supplied for <see cref="IOutboxWriter.AddAsync"/>).</summary>
        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ProcessedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }

        public string? LastError { get; set; }

        public int RetryCount { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
        public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    }

    public enum OutboxMessageStatus
    {
        /// <summary>Waiting for relay or deferred retry (see <see cref="OutboxMessage.NextRetryAt"/>).</summary>
        Pending,
        Processed,
        Failed
    }
}
