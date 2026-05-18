using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class CatalogClientResilienceOptions
{
    public const string SectionName = "Order:CatalogClient:Resilience";

    [Range(1, 1000)]
    public int CbSamplingDurationSeconds { get; init; } = 30;

    [Range(1, 1000)]
    public int CbMinimumThroughput { get; init; } = 10;

    [Range(0.01, 1.0)]
    public double CbFailureRatio { get; init; } = 0.5;

    [Range(1, 600)]
    public int CbBreakDurationSeconds { get; init; } = 10;

    [Range(1, 10)]
    public int RetryMaxAttempts { get; init; } = 2;

    [Range(1, 120)]
    public int AttemptTimeoutSeconds { get; init; } = 3;

    [Range(1, 300)]
    public int TotalTimeoutSeconds { get; init; } = 10;
}
