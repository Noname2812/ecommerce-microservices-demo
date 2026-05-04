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
        /// <summary>
        /// Append a raw payload to the outbox. <paramref name="type"/> must be resolvable via <see cref="System.Type.GetType(string)"/> at publish time
        /// unless you use an assembly-qualified type name.
        /// Does not call <c>SaveChanges</c>; same transaction rules as <see cref="WriteAsync{TEvent}"/>.
        /// </summary>
        Task AddAsync(string type, object payload, CancellationToken cancellationToken = default);

        Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent;
    }
}
