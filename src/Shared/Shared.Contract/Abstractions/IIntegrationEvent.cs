namespace Shared.Contract.Abstractions
{
    /// <summary>
    /// Marker interface for all integration events published to the message bus.
    /// Integration events cross service boundaries and are serialized/deserialized by MassTransit.
    /// </summary>
    public interface IIntegrationEvent
    {
        /// <summary>Unique identifier of this event instance.</summary>
        Guid EventId { get; }

        /// <summary>UTC timestamp when the event was created.</summary>
        DateTimeOffset OccurredOn { get; }

        /// <summary>Name of the service that originated this event.</summary>
        string Source { get; }

        /// <summary>Correlation ID for distributed tracing across services.</summary>
        string? CorrelationId { get; }

        /// <summary>Causation ID — ID of the message that caused this event.</summary>
        string? CausationId { get; }
    }
}
