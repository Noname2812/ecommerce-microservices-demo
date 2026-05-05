using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Kernel.Exceptions;
using Shared.Kernel.Primitives;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Wraps command handlers in a DB transaction. Only applies to <see cref="ICommandBase"/> (not queries).
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
            if (request is IConcurrencyRetriableCommand)
            {
                await _uow.ExecuteInTransactionWithConcurrencyRetryAsync(
                    async () => response = await next(cancellationToken),
                    cancellationToken);
            }
            else
            {
                await _uow.ExecuteInTransactionAsync(
                    async () => response = await next(cancellationToken),
                    cancellationToken);
            }

            _logger.LogInformation("Committed transaction for {RequestName}", requestName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Transaction cancelled for {RequestName}", requestName);
            throw;
        }
        catch (ConcurrencyRetryExhaustedException ex)
        {
            _logger.LogWarning(ex, "Concurrency retry exhausted for {RequestName}", requestName);
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
