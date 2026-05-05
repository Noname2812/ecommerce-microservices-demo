namespace Shared.Kernel.Primitives;

/// <summary>
/// Represents a unit of work that ensures a set of operations
/// are executed within a single transactional boundary.
/// </summary>
/// <remarks>
/// This abstraction allows application code to execute multiple
/// data operations atomically without depending on a specific
/// persistence implementation (e.g., Entity Framework).
/// 
/// Implementations are responsible for:
/// - Starting a database transaction
/// - Executing the provided operation
/// - Committing the transaction if successful
/// - Rolling back the transaction if an exception occurs
/// - Optionally applying retry strategies for transient failures
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Executes the specified operation within a transaction.
    /// </summary>
    /// <param name="operation">
    /// The asynchronous operation to execute inside the transaction.
    /// </param>
    /// <param name="ct">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous transactional operation.
    /// </returns>
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default);

    /// <summary>
    /// Runs the operation in a transaction, retrying optimistic concurrency conflicts when the
    /// persistence layer supports it. Default: single attempt (same as <see cref="ExecuteInTransactionAsync"/>).
    /// </summary>
    Task ExecuteInTransactionWithConcurrencyRetryAsync(Func<Task> operation, CancellationToken ct = default)
    {
        return ExecuteInTransactionAsync(operation, ct);
    }
}