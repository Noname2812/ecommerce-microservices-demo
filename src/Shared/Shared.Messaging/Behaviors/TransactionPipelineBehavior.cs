using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Kernel.Primitives;

namespace Shared.Messaging.Behaviors
{

    /// <summary>
    /// MediatR pipeline behavior that wraps command handlers in a DB transaction.
    /// Only applies to ICommandBase (not queries) to follow CQRS separation.
    /// Works with any DbContext passed via generic param.
    /// </summary>
    public sealed class TransactionPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    {
        private readonly IUnitOfWork _uow;
        private readonly ILogger<TransactionPipelineBehavior<TRequest, TResponse>> _logger;

        public TransactionPipelineBehavior(
            IUnitOfWork uow,
            ILogger<TransactionPipelineBehavior<TRequest, TResponse>> logger)
        {
            _uow = uow;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;

            cancellationToken.ThrowIfCancellationRequested();

            TResponse response = default!;

            _logger.LogInformation("Starting transaction for {RequestName}", requestName);

            try
            {
                await _uow.ExecuteInTransactionAsync(async () =>
                {
                    response = await next(cancellationToken);
                }, cancellationToken);

                _logger.LogInformation("Committed transaction for {RequestName}", requestName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Transaction cancelled for {RequestName}", requestName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed for {RequestName}", requestName);
                throw;
            }

            return response;
        }
    }
}
