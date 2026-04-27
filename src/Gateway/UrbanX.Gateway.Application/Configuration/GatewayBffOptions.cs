using System.ComponentModel.DataAnnotations;

namespace UrbanX.Gateway.Application.Configuration;

public sealed class GatewayBffOptions
{
    public const string SectionName = "Bff";

    [Required]
    public string ClientId { get; set; } = "urbanx-bff";

    [Required]
    public string ClientSecret { get; set; } = "dev-bff-secret";

    public string CookieName { get; set; } = "urbanx.bff";

    public string FrontendUrl { get; set; } = "http://localhost:5173";

    public string SignInPath { get; set; } = "/signin-oidc";

    public string SignOutCallbackPath { get; set; } = "/signout-callback-oidc";

    public string RemoteSignOutPath { get; set; } = "/signout-oidc";

    public IList<string> Scopes { get; set; } = new List<string>
    {
        "openid",
        "profile",
        "email",
        "offline_access",
        "roles",
        "merchant",
        "urbanx-api"
    };

    public int SessionLifetimeMinutes { get; set; } = 60;
}
