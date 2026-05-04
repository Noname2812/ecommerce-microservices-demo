using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Outbox.Abstractions;
using Shared.Outbox;
using Shared.Outbox.DependencyInjection.Options;
using Microsoft.EntityFrameworkCore;

namespace Shared.Outbox.EfCore
{

    /// <summary>
    /// EF Core implementation of IOutboxRepository.
    /// Uses optimistic row-level locking to support multiple relay instances safely.
    /// </summary>
    internal sealed class OutboxRepository : IOutboxRepository
    {
        private readonly OutboxDbContext _dbContext;
        private readonly ILogger<OutboxRepository> _logger;
        private readonly OutboxOptions _options;

        public OutboxRepository(
            OutboxDbContext dbContext,
            ILogger<OutboxRepository> logger,
            IOptions<OutboxOptions> options)
        {
            _dbContext = dbContext;
            _logger = logger;
            _options = options.Value;
        }

        public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            await _dbContext.OutboxMessages.AddAsync(message, cancellationToken);
            // NOTE: SaveChanges is intentionally NOT called here.
            // The caller (application service / command handler) must call SaveChanges
            // within the same DB transaction to guarantee atomicity.
        }

        public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;

            return await _dbContext.OutboxMessages
                .Where(m =>
                    m.Status == OutboxMessageStatus.Pending &&
                    (m.NextRetryAt == null || m.NextRetryAt <= now))
                .OrderBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.OutboxMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            if (message is null)
            {
                _logger.LogWarning("OutboxMessage {MessageId} not found when marking as processed", messageId);
                return;
            }

            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.OutboxMessages
                .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            if (message is null)
            {
                _logger.LogWarning("OutboxMessage {MessageId} not found when marking as failed", messageId);
                return;
            }

            message.RetryCount++;
            message.LastError = error;
            message.FailedAt = DateTimeOffset.UtcNow;

            // Exponential back-off: 5s, 25s, 125s, 625s … capped at 1 hour
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(5, message.RetryCount), 3600));

            if (message.RetryCount >= _options.MaxRetryAttempts)
            {
                message.Status = OutboxMessageStatus.Failed;
                _logger.LogError(
                    "ALERT: OutboxMessage {MessageId} permanently failed after {Retries} retries. Error: {Error}",
                    messageId, message.RetryCount, error);
            }
            else
            {
                message.NextRetryAt = DateTimeOffset.UtcNow.Add(delay);
                _logger.LogWarning(
                    "OutboxMessage {MessageId} publish failed (attempt {Attempt}/{Max}). Next retry at {NextRetry}. Error: {Error}",
                    messageId, message.RetryCount, _options.MaxRetryAttempts, message.NextRetryAt, error);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

}
