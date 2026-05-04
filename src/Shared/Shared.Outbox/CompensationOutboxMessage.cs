namespace Shared.Outbox
{
    /// <summary>
    /// Transactional outbox row for saga compensation events (separate table and relay from main <see cref="OutboxMessage"/>).
    /// </summary>
    public sealed class CompensationOutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Message type resolvable via <see cref="System.Type.GetType(string)"/> at publish time.</summary>
        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? ProcessedAt { get; set; }

        public int RetryCount { get; set; }

        /// <summary>Last failure reason when relay retries or permanently fails (mirrors <see cref="OutboxMessage.LastError"/>).</summary>
        public string? LastError { get; set; }

        public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    }
}
