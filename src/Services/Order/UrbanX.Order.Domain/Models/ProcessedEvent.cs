namespace UrbanX.Order.Domain.Models;

/// <summary>
/// Inbox row for integration-event deduplication. Mirrors Inventory's pattern.
/// Does not inherit BaseEntity — same lifecycle as outbox rows.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = null!;
    public DateTimeOffset ProcessedAt { get; init; }
}
