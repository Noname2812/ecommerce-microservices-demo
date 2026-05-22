using System.ComponentModel.DataAnnotations;

namespace UrbanX.Inventory.Infrastructure.DependencyInjection.Options;

public sealed class ReserveInventoryRequestedConsumerOptions
{
    public const string SectionName = "Inventory:Messaging:ReserveInventoryRequested";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    [Required]
    public ReserveInventoryRequestedRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}

public sealed class ReserveInventoryRequestedRetryOptions
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
