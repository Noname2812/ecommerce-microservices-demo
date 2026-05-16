namespace UrbanX.Gateway.Application.Configuration;

/// <summary>Rate limit configuration. Sliding-window for most partitions; token bucket for flash-sale burst. Bound from "RateLimit".</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimit";

    public GlobalLimit GlobalPerIp { get; set; } = new();
    public GlobalLimit AuthEndpoints { get; set; } = new();
    public GlobalLimit AuthenticatedUser { get; set; } = new();
    public GlobalLimit WriteOperations { get; set; } = new();
    public SalesBurstLimit SalesOrderBurst { get; set; } = new();
    public int SlidingWindowSegments { get; set; } = 6;

    public sealed class GlobalLimit
    {
        public int PermitLimit { get; set; } = 1000;
        public int WindowSeconds { get; set; } = 60;
    }

    public sealed class SalesBurstLimit
    {
        public int TokenLimit { get; set; } = 100;
        public int TokensPerPeriod { get; set; } = 50;
        public int ReplenishmentPeriodSeconds { get; set; } = 1;
    }
}
