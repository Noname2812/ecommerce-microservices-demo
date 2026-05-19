namespace Shared.Cache.Abstractions;

/// <summary>
/// Options for <see cref="ICacheService.GetOrSetAsync{T}(string, Func{System.Threading.CancellationToken, System.Threading.Tasks.Task{T}}, GetOrSetOptions{T}, System.Threading.CancellationToken)"/>.
/// Controls stampede protection, TTL resolution, and negative caching.
/// </summary>
/// <typeparam name="T">Cache value type.</typeparam>
public sealed record GetOrSetOptions<T>
{
    /// <summary>Static TTL. Ignored when <see cref="ExpirySelector"/> is set.</summary>
    public TimeSpan? Expiry { get; init; }

    /// <summary>
    /// Computes TTL from the produced value. Useful when TTL depends on result state
    /// (e.g. terminal vs non-terminal status). Takes precedence over <see cref="Expiry"/>.
    /// </summary>
    public Func<T, TimeSpan>? ExpirySelector { get; init; }

    /// <summary>
    /// When <c>true</c> (default), in-process concurrent misses on the same key are coalesced
    /// to a single factory invocation (SingleFlight pattern). Prevents per-process stampede.
    /// </summary>
    public bool UseSingleFlight { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, acquires a Redis distributed lock around the factory call to prevent
    /// cross-process stampede. Set to <c>true</c> for expensive DB queries; leave <c>false</c>
    /// for cheap queries where lock overhead exceeds the redundant DB cost.
    /// </summary>
    public bool UseDistributedLock { get; init; }

    /// <summary>Lock TTL when <see cref="UseDistributedLock"/> is enabled.</summary>
    public TimeSpan LockExpiry { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum time to wait for the distributed lock. <c>null</c> → block until acquired.</summary>
    public TimeSpan LockWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When set and the factory returns <c>null</c>, the null result is cached for this duration
    /// to avoid repeated DB queries for not-found keys.
    /// </summary>
    public TimeSpan? NegativeTtl { get; init; }
}
