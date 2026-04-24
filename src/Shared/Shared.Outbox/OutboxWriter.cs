using Shared.Outbox.Abstractions;
using System.Text.Json;
using Shared.Contract.Abstractions;

namespace Shared.Outbox
{

    /// <summary>
    /// Default implementation that serializes the event and adds it to the outbox via IOutboxRepository.
    /// </summary>
    internal sealed class OutboxWriter : IOutboxWriter
    {
        private readonly IOutboxRepository _repository;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public OutboxWriter(IOutboxRepository repository)
        {
            _repository = repository;
        }

        public async Task WriteAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent
        {
            // Store the assembly-qualified name so the relay worker can reconstruct the type
            var eventType = typeof(TEvent).AssemblyQualifiedName
                ?? throw new InvalidOperationException($"Cannot get AssemblyQualifiedName for {typeof(TEvent).Name}");

            var outboxMessage = new OutboxMessage
            {
                Id = @event.EventId,
                EventType = eventType,
                Payload = JsonSerializer.Serialize(@event, SerializerOptions),
                CorrelationId = @event.CorrelationId,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _repository.AddAsync(outboxMessage, cancellationToken);
        }
    }
}
