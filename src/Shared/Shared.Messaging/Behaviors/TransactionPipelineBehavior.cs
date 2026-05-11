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
    private static readonly string _requestName = typeof(TRequest).Name;
    private static readonly bool _isConcurrencyRetriable =
        typeof(IConcurrencyRetriableCommand).IsAssignableFrom(typeof(TRequest));

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
        cancellationToken.ThrowIfCancellationRequested();

        TResponse response = default!;
        _logger.LogInformation("Starting transaction for {RequestName}", _requestName);

        try
        {
            if (_isConcurrencyRetriable)
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

            _logger.LogInformation("Committed transaction for {RequestName}", _requestName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Transaction cancelled for {RequestName}", _requestName);
            throw;
        }
        catch (ConcurrencyRetryExhaustedException ex)
        {
            _logger.LogWarning(ex, "Concurrency retry exhausted for {RequestName}", _requestName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed for {RequestName}", _requestName);
            throw;
        }

        return response;
    }
}
