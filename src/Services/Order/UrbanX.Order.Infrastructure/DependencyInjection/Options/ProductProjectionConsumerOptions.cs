using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

/// <summary>
/// Shared retry/throughput tuning for the six Catalog → Order product-projection consumers.
/// One section applies to all because the work is uniform (single upsert into the read-model table).
/// </summary>
public sealed class ProductProjectionConsumerOptions
{
    public const string SectionName = "Order:Messaging:ProductProjection";

    [Required]
    public ProductProjectionRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}

public sealed class ProductProjectionRetryOptions
{
    [Range(0, 100)]
    public int RetryLimit { get; set; } = 3;

    [Range(0, 60_000)]
    public int MinIntervalMs { get; set; } = 200;

    [Range(0, 300_000)]
    public int MaxIntervalMs { get; set; } = 2_000;

    [Range(0, 60_000)]
    public int IntervalDeltaMs { get; set; } = 500;
}
