namespace UrbanX.Gateway.Application.Configuration;

/// <summary>Path prefix and optional HTTP method match for a route that requires a JWT, with optional fine-grained permission checks.</summary>
public sealed class ProtectedRouteEntry
{
    /// <summary>Single method, comma-separated list, or "*" (case-insensitive).</summary>
    public string? Methods { get; set; }

    public string PathPrefix { get; set; } = "/";

    /// <summary>When <see langword="true"/>, only a valid user identity is required.</summary>
    public bool RequireAuthenticatedOnly { get; set; }

    public string? OwnPermission { get; set; }
    public string? AllPermission { get; set; }
    public bool RequiresMfa { get; set; }
}
