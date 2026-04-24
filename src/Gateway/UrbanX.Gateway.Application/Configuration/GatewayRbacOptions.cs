namespace UrbanX.Gateway.Application.Configuration;

/// <summary>RBAC and route access rules. Bound from configuration key <c>GatewayRbac</c> (<see cref="SectionName" />).</summary>
public sealed class GatewayRbacOptions
{
    public const string SectionName = "GatewayRbac";

    /// <summary>Routes that do not require a JWT. Longer prefixes should be more specific; matching is by longest <see cref="PublicRouteEntry.PathPrefix" /> first when evaluated.</summary>
    public List<PublicRouteEntry> Public { get; set; } = new();

    /// <summary>Routes that are not in <see cref="Public" />. First match (longest <see cref="ProtectedRouteEntry.PathPrefix" />) wins.</summary>
    public List<ProtectedRouteEntry> Rules { get; set; } = new();

    /// <summary>When a path is not public and no <see cref="Rules" /> match, the gateway requires a logged-in user (no specific permission). Default is on.</summary>
    public bool FallThroughToAuthenticatedOnly { get; set; } = true;
}
