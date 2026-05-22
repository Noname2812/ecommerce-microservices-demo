using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Infrastructure.DependencyInjection.Options;

public sealed class ClaimCouponRequestedConsumerOptions
{
    public const string SectionName = "Promotion:Messaging:ClaimCouponRequested";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    public ExponentialRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    public int? ConcurrentMessageLimit { get; set; }
}

/// <summary>Maps to <c>UseMessageRetry(r =&gt; r.Exponential(RetryLimit, minInterval, maxInterval, intervalDelta))</c>.</summary>
public sealed class ExponentialRetryOptions
{
    /// <summary>Set to 0 to disable broker retries on the consumer.</summary>
    [Range(0, 100)]
    public int RetryLimit { get; set; } = 3;

    [Range(0, 60000)]
    public int MinIntervalMs { get; set; } = 200;

    [Range(0, 600000)]
    public int MaxIntervalMs { get; set; } = 2000;

    [Range(0, 60000)]
    public int IntervalDeltaMs { get; set; } = 500;
}
