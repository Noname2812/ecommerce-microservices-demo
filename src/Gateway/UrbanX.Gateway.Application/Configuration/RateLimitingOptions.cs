namespace UrbanX.Gateway.Application.Configuration;

/// <summary>Sliding-window style limits (BCL implementation uses sliding segments, see design). Bound from "RateLimit".</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimit";

    public GlobalLimit GlobalPerIp { get; set; } = new();
    public GlobalLimit AuthEndpoints { get; set; } = new();
    public GlobalLimit AuthenticatedUser { get; set; } = new();
    public GlobalLimit WriteOperations { get; set; } = new();
    public GlobalLimit SalesOrderEndpoint { get; set; } = new();
    public int SlidingWindowSegments { get; set; } = 6;

    public sealed class GlobalLimit
    {
        public int PermitLimit { get; set; } = 1000;
        public int WindowSeconds { get; set; } = 60;
    }
}
