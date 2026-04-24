namespace UrbanX.Gateway.Application.Configuration;

/// <summary>JWT parameters for the gateway. Authority takes precedence for metadata discovery. Bound from "Jwt" / "IdentityServer" fallbacks in extension.</summary>
public sealed class GatewayJwtOptions
{
    public const string SectionName = "Jwt";

    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public int ClockSkewSeconds { get; set; } = 30;
}
