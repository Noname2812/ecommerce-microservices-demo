using System.ComponentModel.DataAnnotations;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Configuration for <see cref="InventoryReleaseRequestedConsumerDefinition"/> — queue name, retry, and optional RabbitMQ throughput.
/// Bind section <see cref="SectionName"/> from appsettings.
/// </summary>
public sealed class InventoryReleaseRequestedConsumerOptions
{
    public const string SectionName = "Inventory:Messaging:InventoryReleaseRequested";

    /// <summary>
    /// Receive endpoint (queue) name. Leave unset or empty to use MassTransit endpoint name formatter defaults.
    /// </summary>
    [MaxLength(255)]
    public string? QueueName { get; set; }

    /// <summary>Broker-level retry for transient failures (MassTransit <c>UseMessageRetry</c>).</summary>
    [Required]
    public InventoryReleaseRequestedRetryOptions Retry { get; set; } = new();

    /// <summary>RabbitMQ QoS prefetch for this endpoint only; omit for transport default.</summary>
    public ushort? PrefetchCount { get; set; }

    /// <summary>Concurrent message limit for this endpoint; omit for transport default.</summary>
    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}

/// <summary>Maps to <c>UseMessageRetry(r =&gt; r.Interval(Intervals, TimeSpan.FromSeconds(IntervalSeconds)))</c>.</summary>
public sealed class InventoryReleaseRequestedRetryOptions
{
    /// <summary>
    /// Number of retry intervals. Set to 0 (or <see cref="IntervalSeconds"/> to 0) to disable broker retries on this consumer.
    /// </summary>
    [Range(0, 100)]
    public int Intervals { get; set; } = 3;

    /// <summary>Delay between retries within each interval.</summary>
    [Range(0, 3600)]
    public int IntervalSeconds { get; set; } = 5;
}
