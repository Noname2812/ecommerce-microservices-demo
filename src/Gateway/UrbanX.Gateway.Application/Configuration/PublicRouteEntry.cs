namespace UrbanX.Gateway.Application.Configuration;

/// <summary>HTTP method and path prefix match for a route that is accessible without a JWT (prefix match, segment-aware).</summary>
public sealed class PublicRouteEntry
{
    /// <summary>Single method or "*" for any (case-insensitive).</summary>
    public string Method { get; set; } = "*";

    public string PathPrefix { get; set; } = "/";
}
