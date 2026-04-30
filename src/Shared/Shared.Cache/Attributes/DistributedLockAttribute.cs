namespace Shared.Cache.Attributes;

/// <summary>
/// Apply to a MediatR Command or Query to acquire a distributed Redis lock before the handler runs.
/// The lock is released automatically after the handler completes (or fails).
/// <para>
/// <b>ResourceTemplate</b> supports <c>{PropertyName}</c> placeholders that are replaced
/// with the corresponding property value from the request at runtime.
/// Example: <c>[DistributedLock("order:checkout:{UserId}")]</c>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DistributedLockAttribute : Attribute
{
    public string ResourceTemplate { get; }

    /// <summary>How long the lock is held in Redis (TTL). Default: 30 seconds.</summary>
    public int ExpirySeconds { get; init; } = 30;

    /// <summary>How long to wait for the lock before giving up. Default: 5 seconds.</summary>
    public int WaitTimeoutSeconds { get; init; } = 5;

    public DistributedLockAttribute(string resourceTemplate)
    {
        ResourceTemplate = resourceTemplate;
    }
}
