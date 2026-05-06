namespace UrbanX.Promotion.Application.Abstractions;

/// <summary>
/// Queues side effects that must run only after SQL transaction commits.
/// </summary>
/// <remarks>
/// Implementation is scoped per request; Promotion persistence <c>EfUnitOfWork</c> drains the queue immediately after SQL <c>COMMIT</c>.
/// Failures surfaced from <see cref="FlushAsync"/> mean the DB transaction already committed — compensating workflows (metrics, alerts, manual Redis reconcile) may be required.
/// </remarks>
public interface IPostCommitTaskQueue
{
    void Enqueue(Func<CancellationToken, Task> work);

    /// <summary>Discard queued work after SQL rollback.</summary>
    void DiscardPending();

    /// <summary>Execute queued work after successful SQL commit (sequential).</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
