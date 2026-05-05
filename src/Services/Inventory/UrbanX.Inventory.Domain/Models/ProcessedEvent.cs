namespace UrbanX.Inventory.Domain.Models;

/// <summary>
/// Tracks integration events handled exactly-once (inbox / consumer deduplication).
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; }
}
