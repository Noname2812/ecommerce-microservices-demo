using System.ComponentModel.DataAnnotations;

namespace UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

public sealed class CatalogProjectionConsumerOptions
{
    public const string SectionName = "Catalog:Messaging:ProductProjection";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    [Required]
    public CatalogProjectionConsumerRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}

public sealed class CatalogProjectionConsumerRetryOptions
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
