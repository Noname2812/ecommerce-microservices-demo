using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Application.DependencyInjection.Options;

public sealed class RedeemSalePromotionRequestedConsumerOptions
{
    public const string SectionName = "Promotion:Messaging:RedeemSalePromotionRequested";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    public ExponentialRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    public int? ConcurrentMessageLimit { get; set; }
}
