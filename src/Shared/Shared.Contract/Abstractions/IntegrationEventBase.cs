namespace Shared.Contract.Abstractions
{
    /// <summary>
    /// Base record for all integration events. Inherit from this to create typed events.
    /// </summary>
    public abstract record IntegrationEventBase : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
        public abstract string Source { get; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
