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
                // Identity (OIDC / management) — must be reachable before any JWT
                new PublicRouteEntry { PathPrefix = "/connect/authorize", Method = "GET,HEAD" },
                new PublicRouteEntry { PathPrefix = "/connect/token", Method = "POST" },
                new PublicRouteEntry { PathPrefix = "/connect/endsession", Method = "GET,HEAD" },
                new PublicRouteEntry { PathPrefix = "/api/account/register", Method = "POST" }
            ];
        }
    }
}
