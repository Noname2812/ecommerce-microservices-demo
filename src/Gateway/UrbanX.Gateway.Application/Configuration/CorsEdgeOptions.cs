using System.ComponentModel.DataAnnotations;

namespace UrbanX.Gateway.Application.Configuration;

public sealed class CorsEdgeOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:3000", "http://localhost:5173", "http://localhost:5174"];

    public string[] AllowedMethods { get; set; } =
    [
        "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"
    ];

    public string[] AllowedHeaders { get; set; } = ["Authorization", "Content-Type", "X-Request-Id"];

    public string[] ExposedHeaders { get; set; } = ["X-Request-Id", "X-RateLimit-Remaining"];

    public bool AllowCredentials { get; set; } = true;

    /// <summary>Preflight cache in seconds (Access-Control-Max-Age).</summary>
    [Range(0, 86_400)]
    public int PreflightMaxAgeSeconds { get; set; } = 3600;
}
