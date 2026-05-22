using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Abstractions;
using Shared.Kernel.Exceptions;
using Shared.Kernel.Primitives;

namespace Shared.Messaging.Behaviors;

/// <summary>
/// Wraps command handlers in a DB transaction. Only applies to <see cref="ICommandBase"/> (not queries).
///
/// <para>
/// Handlers that mutate the database before reaching a <see cref="Result.Failure(Error)"/> branch
/// (e.g. atomic CAS UPDATEs that persist between validation steps) need the transaction to roll back
/// rather than commit the partial writes. We translate <c>IResult.IsFailure</c> into an internal
/// exception so the unit-of-work's existing exception path rolls back, then catch it here and return
/// the original failure to the caller.
/// </para>
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
                    () => RunAndAbortOnFailureAsync(next, r => response = r, cancellationToken),
                    cancellationToken);
            }
            else
            {
                await _uow.ExecuteInTransactionAsync(
                    () => RunAndAbortOnFailureAsync(next, r => response = r, cancellationToken),
                    cancellationToken);
            }

            _logger.LogInformation("Committed transaction for {RequestName}", _requestName);
        }
        catch (HandlerFailureAbortException)
        {
            // Handler returned IResult.IsFailure — the unit-of-work already rolled back any pending
            // DB writes (including atomic UPDATEs applied earlier in the same transaction).
            _logger.LogInformation(
                "Rolled back transaction for {RequestName} because handler returned a failure result",
                _requestName);
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

    private static async Task RunAndAbortOnFailureAsync(
        RequestHandlerDelegate<TResponse> next,
        Action<TResponse> capture,
        CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken);
        capture(result);

        // Triggers the unit-of-work's catch-all rollback path so any atomic UPDATEs already applied
        // in this transaction are reverted. The pipeline catches this exception type specifically and
        // returns the captured response to the caller — the failure surfaces as a Result, not an exception.
        if (result is IResult { IsFailure: true })
            throw new HandlerFailureAbortException();
    }

    private sealed class HandlerFailureAbortException : Exception;
}
