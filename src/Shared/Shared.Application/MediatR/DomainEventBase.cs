namespace Shared.Application
{
    /// <summary>
    /// Base record for domain events.
    /// </summary>
    public abstract record DomainEventBase : IDomainEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
    }
}
