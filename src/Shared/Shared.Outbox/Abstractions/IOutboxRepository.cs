using Shared.Outbox;

namespace Shared.Outbox.Abstractions
{
    /// <summary>
    /// Repository for managing outbox messages.
    /// </summary>
    public interface IOutboxRepository
    {
        Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default);
        Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);
        Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
    }
}
