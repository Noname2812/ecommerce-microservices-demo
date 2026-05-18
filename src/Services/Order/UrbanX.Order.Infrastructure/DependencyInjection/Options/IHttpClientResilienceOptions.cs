namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

/// <summary>
/// Common shape for outbound HTTP client resilience options. Lets the DI layer share a single
/// <c>ApplyResilience</c> helper across services (Catalog, Promotion, …) instead of duplicating
/// per-client copies of the same code.
/// </summary>
public interface IHttpClientResilienceOptions
{
    int CbSamplingDurationSeconds { get; }
    int CbMinimumThroughput { get; }
    double CbFailureRatio { get; }
    int CbBreakDurationSeconds { get; }
    int RetryMaxAttempts { get; }
    int AttemptTimeoutSeconds { get; }
    int TotalTimeoutSeconds { get; }
}
