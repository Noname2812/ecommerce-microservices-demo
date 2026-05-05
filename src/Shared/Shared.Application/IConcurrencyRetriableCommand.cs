namespace Shared.Application;

/// <summary>
/// Marker for commands whose handler updates concurrency-token entities (e.g. xmin).
/// The transaction pipeline retries up to 3 attempts on <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
/// </summary>
public interface IConcurrencyRetriableCommand : ICommandBase
{
}
