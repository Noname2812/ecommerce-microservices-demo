using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Options;

public sealed class CreatePaymentSessionConsumerOptions
{
    public const string SectionName = "Payment:Messaging:CreatePaymentSession";

    [MaxLength(255)]
    public string? QueueName { get; set; }

    public ExponentialRetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }
}
