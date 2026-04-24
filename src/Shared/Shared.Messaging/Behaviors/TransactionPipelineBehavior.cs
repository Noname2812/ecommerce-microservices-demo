using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Shared.Messaging.Behaviors
{

    /// <summary>
    /// MediatR pipeline behavior that wraps command handlers in a DB transaction.
    /// Only applies to ICommandBase (not queries) to follow CQRS separation.
    /// Works with any DbContext passed via generic param.
    /// </summary>
    public abstract class TransactionPipelineBehavior<TRequest, TResponse, TDbContext>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : ICommandBase
        where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly ILogger<TransactionPipelineBehavior<TRequest, TResponse, TDbContext>> _logger;

        public TransactionPipelineBehavior(
            TDbContext dbContext,
            ILogger<TransactionPipelineBehavior<TRequest, TResponse, TDbContext>> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                _logger.LogDebug("Started transaction for {RequestName}", requestName);

                try
                {
                    var response = await next();

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogDebug("Committed transaction for {RequestName}", requestName);
                    return response;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Rolled back transaction for {RequestName}", requestName);
                    throw;
                }
            });
        }
    }
}
