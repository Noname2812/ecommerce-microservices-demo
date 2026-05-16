using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Application.DependencyInjection.Options;

public sealed class ClaimCouponRequestedConsumerOptions
{
    public const string SectionName = "Promotion:Messaging:ClaimCouponRequested";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    public ExponentialRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    public int? ConcurrentMessageLimit { get; set; }
}
