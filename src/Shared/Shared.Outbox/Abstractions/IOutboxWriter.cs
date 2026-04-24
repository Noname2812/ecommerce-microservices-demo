using Shared.Contract.Abstractions;

namespace Shared.Outbox.Abstractions
{

    /// <summary>
    /// Service for writing integration events to the outbox table.
    /// Must be called within the same DbContext transaction as your aggregate changes.
    ///
    /// Example usage inside a command handler:
    /// <code>
    /// await _outboxWriter.WriteAsync(new OrderCreatedEvent(...), cancellationToken);
    /// await _dbContext.SaveChangesAsync(cancellationToken); // commits both aggregate + outbox atomically
    /// </code>
    /// </summary>
    public interface IOutboxWriter
    {
        Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent;
    }
}
