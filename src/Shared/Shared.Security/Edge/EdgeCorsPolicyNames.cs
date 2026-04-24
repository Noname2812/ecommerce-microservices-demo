namespace Shared.Security.Edge;

/// <summary>
/// Central policy name for the UrbanX edge (gateway). Services should not use wildcards with
/// <see cref="CorsEdgeOptions.AllowCredentials" />; origins must be explicit.
/// </summary>
public static class EdgeCorsPolicyNames
{
    public const string Default = "UrbanX.EdgeCors";
}
