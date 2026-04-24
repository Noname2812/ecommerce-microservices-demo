using System.Security.Claims;
using System.Text.Json;

namespace UrbanX.Gateway.Infrastructure.Rbac;

public static class PermissionClaimReader
{
    private const string JsonClaim = "permissions";

    public static IReadOnlyList<string> GetPermissionStrings(ClaimsPrincipal user)
    {
        if (user.Identity is not { IsAuthenticated: true })
        {
            return Array.Empty<string>();
        }

        var fromClaims = user.FindAll("permission").Select(c => c.Value).ToList();
        if (fromClaims is { Count: > 0 })
        {
            return fromClaims;
        }

        var json = user.FindFirstValue(JsonClaim);
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool IsWildcardAdmin(IReadOnlyList<string> permissions) =>
        permissions.Any(static p => string.Equals(p, "*:*:*", StringComparison.Ordinal));

    public static bool MfaSatisfied(ClaimsPrincipal user) =>
        string.Equals(user.FindFirst("mfa_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(user.FindFirst("mfa")?.Value, "true", StringComparison.OrdinalIgnoreCase);
}
