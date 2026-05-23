using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

/// <summary>
/// Tuning for the cache-invalidation consumers (<c>OrderConfirmed</c> + <c>OrderCancelled</c>).
/// Both consumers share retry/throughput knobs; their queue names are kept distinct so they can be
/// scaled independently by RabbitMQ.
/// </summary>
public sealed class OrderTerminalStatusCacheConsumerOptions
{
    public const string SectionName = "Order:Messaging:TerminalStatusCache";

    [MaxLength(255)]
    public string? ConfirmedQueueName { get; set; }

    [MaxLength(255)]
    public string? CancelledQueueName { get; set; }

    [Required]
    public RetryOptions Retry { get; set; } = new();

    public ushort? PrefetchCount { get; set; }

    [Range(1, 1024)]
    public int? ConcurrentMessageLimit { get; set; }

    public sealed class RetryOptions
    {
        [Range(0, 20)]
        public int RetryLimit { get; set; } = 3;

        [Range(0, 600_000)]
        public int MinIntervalMs { get; set; } = 1_000;

        [Range(0, 600_000)]
        public int MaxIntervalMs { get; set; } = 30_000;

        [Range(0, 600_000)]
        public int IntervalDeltaMs { get; set; } = 1_000;
    }
}

public sealed class OrderTerminalStatusCacheConsumerOptionsValidator
    : IValidateOptions<OrderTerminalStatusCacheConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, OrderTerminalStatusCacheConsumerOptions options)
    {
        if (options.Retry is null)
            return ValidateOptionsResult.Fail("Retry section is required.");

        if (options.Retry.MinIntervalMs > options.Retry.MaxIntervalMs)
            return ValidateOptionsResult.Fail(
                "Retry.MinIntervalMs must be <= Retry.MaxIntervalMs.");

        return ValidateOptionsResult.Success;
    }
}
