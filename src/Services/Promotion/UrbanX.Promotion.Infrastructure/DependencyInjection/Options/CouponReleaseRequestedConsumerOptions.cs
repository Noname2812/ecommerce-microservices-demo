using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Infrastructure.DependencyInjection.Options;

/// <summary>
/// Receive endpoint tuning for coupon release requested consumer endpoint:
/// queue bind to fanout <c>compensation.events</c>, optional retry and RabbitMQ throughput.
/// </summary>
public sealed class CouponReleaseRequestedConsumerOptions
{
    public const string SectionName = "Promotion:Messaging:CouponReleaseRequested";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    /// <summary>Always non-null in practice; initializer supplies defaults when the config section omits nested values.</summary>
    public CouponReleaseRequestedRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    public int? ConcurrentMessageLimit { get; set; }
}

/// <summary>Maps to <c>UseMessageRetry(r =&gt; r.Interval(Intervals, TimeSpan.FromSeconds(IntervalSeconds)))</c>.</summary>
public sealed class CouponReleaseRequestedRetryOptions
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
