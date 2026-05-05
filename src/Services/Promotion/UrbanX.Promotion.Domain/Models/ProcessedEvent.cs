namespace UrbanX.Promotion.Domain.Models;

/// <summary>
/// Inbox row for integration-event deduplication.
/// Intentionally does not inherit BaseEntity — same pattern as outbox rows.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = null!;
    public DateTimeOffset ProcessedAt { get; init; }
}
