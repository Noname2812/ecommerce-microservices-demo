namespace Shared.Cache.Abstractions;

public interface ILockHandle : IAsyncDisposable
{
    bool IsAcquired { get; }
    string Resource { get; }
}

public interface IDistributedLockService
{
    /// <summary>
    /// Try to acquire lock immediately. Returns null if the resource is already locked.
    /// </summary>
    Task<ILockHandle?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Poll until lock is acquired or <paramref name="waitTimeout"/> is exceeded.
    /// Returns null on timeout.
    /// </summary>
    Task<ILockHandle?> AcquireAsync(string resource, TimeSpan expiry, TimeSpan waitTimeout, CancellationToken ct = default);
}
