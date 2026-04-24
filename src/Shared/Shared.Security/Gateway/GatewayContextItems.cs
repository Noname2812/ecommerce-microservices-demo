namespace Shared.Security.Gateway;

/// <summary>Keys in <c>HttpContext.Items</c> for gateway flow.</summary>
public static class GatewayContextItems
{
    /// <summary>Permission resolution result for the current route: "own" or "all" (enrichment / downstream trust).</summary>
    public const string PermissionScope = "PermissionScope";
}
