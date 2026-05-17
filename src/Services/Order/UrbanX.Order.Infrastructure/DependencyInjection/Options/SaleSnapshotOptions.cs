using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class SaleSnapshotOptions
{
    public const string SectionName = "Order:SaleSnapshot";

    /// <summary>TTL of L1 in-process memory cache for campaign meta + sale prices. Keep short — Redis is the source of truth.</summary>
    [Range(1, 300)]
    public int MemoryCacheTtlSeconds { get; init; } = 10;
}
