using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Options;

public sealed class ExponentialRetryOptions
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
