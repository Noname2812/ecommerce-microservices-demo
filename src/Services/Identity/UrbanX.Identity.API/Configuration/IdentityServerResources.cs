using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace UrbanX.Identity.API.Configuration;

internal static class IdentityServerResources
{
    public const string ApiScope = "urbanx-api";
    public const string SpaClient = "urbanx-spa";
    public const string GatewayClient = "urbanx-gateway";

    public static IEnumerable<IdentityResource> IdentityResources => new IdentityResource[]
    {
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "User roles", new[] { "role" }),
        new IdentityResource("merchant", "Merchant identifier", new[] { "merchant_id" })
    };

    public static IEnumerable<ApiScope> ApiScopes => new[]
    {
        new ApiScope(ApiScope, "UrbanX API")
    };

    public static IEnumerable<ApiResource> ApiResources => new[]
    {
        new ApiResource(ApiScope, "UrbanX API")
        {
            Scopes = { ApiScope },
            UserClaims = { "role", "merchant_id", "email" }
        }
    };

    public static IEnumerable<Client> Clients(IConfiguration config) => new[]
    {
        new Client
        {
            ClientId = SpaClient,
            ClientName = "UrbanX SPA",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,
            AllowOfflineAccess = true,
            RefreshTokenUsage = TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = TokenExpiration.Sliding,
            SlidingRefreshTokenLifetime = (int)TimeSpan.FromDays(7).TotalSeconds,
            AccessTokenLifetime = (int)TimeSpan.FromMinutes(60).TotalSeconds,
            RedirectUris = { "http://localhost:5173/auth/callback" },
            PostLogoutRedirectUris = { "http://localhost:5173/" },
            AllowedCorsOrigins = { "http://localhost:5173" },
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                IdentityServerConstants.StandardScopes.OfflineAccess,
                "roles",
                "merchant",
                ApiScope
            }
        },
        new Client
        {
            ClientId = "urbanx-test-password",
            ClientName = "Test password client (dev only)",
            AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
            ClientSecrets = { new Secret("dev-secret".Sha256()) },
            AllowOfflineAccess = true,
            AccessTokenLifetime = (int)TimeSpan.FromMinutes(60).TotalSeconds,
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email,
                IdentityServerConstants.StandardScopes.OfflineAccess,
                "roles",
                "merchant",
                ApiScope
            }
        }
    };
}
