using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using UrbanX.Gateway.Application.Constants;
using UrbanX.Gateway.Application.Abstractions;
using Shared.Kernel.Constants;

namespace UrbanX.Gateway.Infrastructure.Enrichment;

public sealed class RequestHeaderEnricher : IRequestHeaderEnricher
{
    public void Apply(HttpContext http)
    {
        var id = GetOrSetRequestId(http);
        if (!http.Response.HasStarted)
        {
            http.Response.Headers[GatewayHeaderNames.XRequestId] = id;
        }

        if (http.User.Identity is { IsAuthenticated: true })
        {
            var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? http.User.FindFirst("sub")?.Value;

            var roleParts = new List<string>();
            roleParts.AddRange(http.User.FindAll("role").Select(c => c.Value));
            var r = http.User.FindFirst("roles")?.Value;
            if (!string.IsNullOrEmpty(r))
            {
                roleParts.Add(r);
            }

            if (!string.IsNullOrEmpty(sub))
            {
                http.Request.Headers[GatewayHeaderNames.XUserId] = sub;
            }

            if (roleParts.Count > 0)
            {
                http.Request.Headers[GatewayHeaderNames.XUserRoles] = string.Join(",", roleParts);
            }

            var merchant = http.User.FindFirst("merchant_id")?.Value;
            if (!string.IsNullOrEmpty(merchant))
            {
                http.Request.Headers[GatewayHeaderNames.XMerchantId] = merchant;
            }

            var scope = (http.Items[GatewayContextItems.PermissionScope] as string) ?? "own";
            http.Request.Headers[GatewayHeaderNames.XPermissionScope] = scope;
        }

        var isSePayWebhook = http.Request.Path.StartsWithSegments("/api/v1/payments/webhook", StringComparison.OrdinalIgnoreCase);

        http.Request.Headers.Remove("Cookie");
        if (!isSePayWebhook)
            http.Request.Headers.Remove("Authorization");

        if (string.IsNullOrEmpty(http.Request.Headers[GatewayHeaderNames.XForwardedFor].ToString()) && http.Connection.RemoteIpAddress is { } client)
        {
            var a = client.AddressFamily is System.Net.Sockets.AddressFamily.InterNetworkV6 && client.IsIPv4MappedToIPv6
                ? client.MapToIPv4()
                : client;
            http.Request.Headers[GatewayHeaderNames.XForwardedFor] = a.ToString();
        }

        if (string.IsNullOrEmpty(http.Request.Headers[GatewayHeaderNames.XForwardedHost].ToString()) && http.Request.Host.HasValue)
        {
            http.Request.Headers[GatewayHeaderNames.XForwardedHost] = http.Request.Host.Value;
        }
    }

    private static string GetOrSetRequestId(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue(GatewayHeaderNames.XRequestId, out var h) && !StringValues.IsNullOrEmpty(h))
        {
            return h.ToString();
        }

        var g = Guid.NewGuid().ToString("D");
        http.Request.Headers[GatewayHeaderNames.XRequestId] = g;
        return g;
    }
}
