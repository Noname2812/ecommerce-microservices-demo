using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class CatalogSnapshotOptions
{
    public const string SectionName = "Order:CatalogSnapshot";

    /// <summary>TTL of L1 Redis entries for product/variant snapshots.</summary>
    [Range(1, 86400)]
    public int CacheTtlSeconds { get; init; } = 120;

    /// <summary>Timeout for L3 HTTP fallback to the Catalog service. Short on purpose — L1+L2 own the hot path.</summary>
    [Range(50, 5000)]
    public int HttpFallbackTimeoutMilliseconds { get; init; } = 300;
}
