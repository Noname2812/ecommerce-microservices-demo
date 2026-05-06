using UrbanX.Promotion.Application.Abstractions;

namespace UrbanX.Promotion.Persistence;

/// <inheritdoc />
/// <remarks>Scoped per HTTP request — single-threaded; no locking required.</remarks>
public sealed class PostCommitTaskQueue : IPostCommitTaskQueue
{
    private readonly List<Func<CancellationToken, Task>> _pending = new();

    public void Enqueue(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _pending.Add(work);
    }

    public void DiscardPending() => _pending.Clear();

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var batch = new List<Func<CancellationToken, Task>>(_pending);
        _pending.Clear();

        foreach (var work in batch)
            await work(cancellationToken);
    }
}
