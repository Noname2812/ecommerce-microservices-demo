using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Outbox.Abstractions;
using Shared.Outbox.DependencyInjection.Options;

namespace Shared.Outbox.EfCore
{
    internal sealed class CompensationOutboxRepository : ICompensationOutboxRepository
    {
        private readonly OutboxDbContext _dbContext;
        private readonly ILogger<CompensationOutboxRepository> _logger;
        private readonly CompensationOutboxOptions _options;

        public CompensationOutboxRepository(
            OutboxDbContext dbContext,
            ILogger<CompensationOutboxRepository> logger,
            IOptions<CompensationOutboxOptions> options)
        {
            _dbContext = dbContext;
            _logger = logger;
            _options = options.Value;
        }

        public async Task AddAsync(CompensationOutboxMessage message, CancellationToken cancellationToken = default)
        {
            await _dbContext.Set<CompensationOutboxMessage>().AddAsync(message, cancellationToken);
        }

        public async Task<IReadOnlyList<CompensationOutboxMessage>> GetUnprocessedAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Set<CompensationOutboxMessage>()
                .Where(m => m.Status == OutboxMessageStatus.Pending)
                .OrderBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.Set<CompensationOutboxMessage>()
                .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            if (message is null)
            {
                _logger.LogWarning(
                    "CompensationOutboxMessage {MessageId} not found when marking as processed",
                    messageId);
                return;
            }

            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAt = DateTimeOffset.UtcNow;
            message.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
        {
            var message = await _dbContext.Set<CompensationOutboxMessage>()
                .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            if (message is null)
            {
                _logger.LogWarning(
                    "CompensationOutboxMessage {MessageId} not found when marking as failed",
                    messageId);
                return;
            }

            message.RetryCount++;
            message.LastError = error.Length > 2000 ? error[..2000] : error;

            if (message.RetryCount >= _options.MaxRetryAttempts)
            {
                message.Status = OutboxMessageStatus.Failed;
                _logger.LogError(
                    "ALERT: CompensationOutboxMessage {MessageId} permanently failed after {Retries} retries. Error: {Error}",
                    messageId, message.RetryCount, error);
            }
            else
            {
                _logger.LogWarning(
                    "CompensationOutboxMessage {MessageId} publish failed (attempt {Attempt}/{Max}). Error: {Error}",
                    messageId, message.RetryCount, _options.MaxRetryAttempts, error);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
