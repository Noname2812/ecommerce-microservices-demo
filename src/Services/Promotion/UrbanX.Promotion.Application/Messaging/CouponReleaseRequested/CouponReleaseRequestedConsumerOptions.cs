using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

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
