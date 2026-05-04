using Shared.Outbox;

namespace Shared.Outbox.Abstractions
{
    public interface ICompensationOutboxRepository
    {
        Task AddAsync(CompensationOutboxMessage message, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CompensationOutboxMessage>> GetUnprocessedAsync(
            int batchSize,
            CancellationToken cancellationToken = default);

        Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

        Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
    }
}
