namespace Shared.Cache.Attributes;

/// <summary>
/// Apply to a MediatR Query to enable cache-aside with stampede prevention.
/// On a cache miss, only one request acquires a lock and fetches from DB;
/// concurrent requests for the same key wait for the lock and read from cache.
/// <para>
/// <b>KeyTemplate</b> supports <c>{PropertyName}</c> placeholders resolved from the request.
/// Example: <c>[CacheQuery("product:detail:{Id}")]</c>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CacheQueryAttribute : Attribute
{
    public string KeyTemplate { get; }

    /// <summary>Cache TTL. Default: 300 seconds (5 minutes).</summary>
    public int ExpirySeconds { get; init; } = 300;

    /// <summary>How long the distributed lock is held in Redis. Default: 10 seconds.</summary>
    public int LockExpirySeconds { get; init; } = 10;

    /// <summary>
    /// How long a waiting request polls for the lock before falling back to the handler.
    /// Default: 5 seconds.
    /// </summary>
    public int LockWaitTimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// TTL for failure (not-found) results. 0 = disabled (default).
    /// Enable to prevent repeated DB hits for resources that don't exist.
    /// </summary>
    public int NegativeTtlSeconds { get; init; } = 0;

    /// <summary>
    /// Random jitter applied to expiry as a percentage (0–100). Default: 10 (±10%).
    /// Prevents thundering herd when many cache entries expire at the same instant.
    /// </summary>
    public int JitterPercent { get; init; } = 10;

    public CacheQueryAttribute(string keyTemplate)
    {
        KeyTemplate = keyTemplate;
    }
}
