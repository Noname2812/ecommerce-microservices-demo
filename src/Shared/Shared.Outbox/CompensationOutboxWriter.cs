using System.Text.Json;
using Shared.Outbox.Abstractions;

namespace Shared.Outbox
{
    internal sealed class CompensationOutboxWriter : ICompensationOutboxWriter
    {
        private readonly ICompensationOutboxRepository _repository;

        public CompensationOutboxWriter(ICompensationOutboxRepository repository)
        {
            _repository = repository;
        }

        public Task AddAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class
        {
            ArgumentNullException.ThrowIfNull(payload);
            var eventType = typeof(T).AssemblyQualifiedName
                ?? throw new InvalidOperationException($"Cannot get AssemblyQualifiedName for {typeof(T).Name}");
            return AddAsync(eventType, payload, cancellationToken);
        }

        public async Task AddAsync(string type, object payload, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            ArgumentNullException.ThrowIfNull(payload);

            var row = new CompensationOutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = type,
                Payload = JsonSerializer.Serialize(
                    payload, payload.GetType(), CompensationOutboxJsonSerializerOptions.Default),
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _repository.AddAsync(row, cancellationToken);
        }
    }
}
