namespace UrbanX.Inventory.Domain.Models;

/// <summary>
/// Inbox row for integration-event deduplication (not a rich domain aggregate).
/// Intentionally does not inherit BaseEntity — same pattern as outbox rows.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = null!;
    public DateTimeOffset ProcessedAt { get; init; }
}
