namespace Shared.Cache.DependencyInjection.Options;

public sealed class CacheOptions
{
    public const string SectionName = "Shared:Cache";

    /// <summary>Prefix applied to all cache keys: <c>{InstanceName}:{key}</c>.</summary>
    public string InstanceName { get; set; } = "urbanx";

    /// <summary>Default TTL when no expiry is supplied to SetAsync / GetOrSetAsync.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);
}
