using Microsoft.Extensions.Options;
using UrbanX.Gateway.Application.Configuration;

namespace UrbanX.Gateway.Infrastructure.Rbac;

public sealed class GatewayRbacOptionsSetup : IConfigureOptions<GatewayRbacOptions>
{
    public void Configure(GatewayRbacOptions o)
    {
        o.Public ??= new();
        if (o.Public.Count is 0)
        {
            o.Public =
            [
                new PublicRouteEntry { PathPrefix = "/health", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/health", Method = "HEAD" },
                new PublicRouteEntry { PathPrefix = "/alive", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/.well-known", Method = "*" },
                new PublicRouteEntry { PathPrefix = "/api/v1/catalog", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/api/v1/promotions/preview", Method = "POST" },
                // Identity (OIDC / management) — must be reachable before any JWT
                new PublicRouteEntry { PathPrefix = "/connect/authorize", Method = "GET,HEAD" },
                new PublicRouteEntry { PathPrefix = "/connect/token", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/connect/endsession", Method = "GET,HEAD" },
                new PublicRouteEntry { PathPrefix = "/connect/userinfo", Method = "GET,POST" },
                new PublicRouteEntry { PathPrefix = "/connect/revocation", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/connect/introspect", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/signin-google", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/api/account/register", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/api/account/confirm-email", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/api/account/forgot-password", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/api/account/reset-password", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/api/account/external", Method = "GET" },
                // BFF management endpoints + OIDC callbacks (login/logout flow)
                new PublicRouteEntry { PathPrefix = "/bff/login", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/bff/silent-login", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/bff/silent-login-callback", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/bff/logout", Method = "GET,POST" },
                new PublicRouteEntry { PathPrefix = "/bff/user", Method = "GET" },
                new PublicRouteEntry { PathPrefix = "/signin-oidc", Method = "GET,POST" },
                new PublicRouteEntry { PathPrefix = "/signout-oidc", Method = "GET,POST" },
                new PublicRouteEntry { PathPrefix = "/signout-callback-oidc", Method = "GET,POST" },
                // Test endpoints
                new PublicRouteEntry { PathPrefix = "/api/v1/orders", Method = "POST" },
            ];
        }
    }
}
