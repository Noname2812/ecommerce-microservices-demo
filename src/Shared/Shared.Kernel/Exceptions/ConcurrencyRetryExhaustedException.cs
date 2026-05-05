namespace Shared.Kernel.Exceptions;

/// <summary>
/// Thrown when optimistic concurrency retries are exhausted (e.g. EF xmin conflicts).
/// Map to HTTP 503 at the API boundary.
/// </summary>
public sealed class ConcurrencyRetryExhaustedException : Exception
{
    public ConcurrencyRetryExhaustedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
