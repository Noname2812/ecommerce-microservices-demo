using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using UrbanX.Gateway.Application.Abstractions;
using UrbanX.Gateway.Application.Configuration;

namespace UrbanX.Gateway.Infrastructure.Rbac;

public sealed class EndpointAccessRegistry : IEndpointAccessRegistry
{
    private readonly IReadOnlyList<PublicRouteEntry> _publicSorted;
    private readonly IReadOnlyList<ProtectedRouteEntry> _rulesSorted;
    private readonly GatewayRbacOptions _opt;

    public EndpointAccessRegistry(IOptions<GatewayRbacOptions> options)
    {
        _opt = options.Value;
        _publicSorted = _opt.Public
            .OrderByDescending(static r => r.PathPrefix?.Length ?? 0)
            .ToList();
        _rulesSorted = _opt.Rules
            .OrderByDescending(static r => r.PathPrefix?.Length ?? 0)
            .ToList();
    }

    public EndpointAccessResult GetAccessFor(string method, PathString path)
    {
        foreach (var e in _publicSorted)
        {
            if (MatchesPath(path, e.PathPrefix) && MethodMatches(e.Method, method))
            {
                return EndpointAccessResult.ForPublic();
            }
        }

        foreach (var r in _rulesSorted)
        {
            if (MatchesPath(path, r.PathPrefix) && MethodMatches(r.Methods, method))
            {
                if (r.RequireAuthenticatedOnly
                    || (string.IsNullOrEmpty(r.OwnPermission) && string.IsNullOrEmpty(r.AllPermission)))
                {
                    return EndpointAccessResult.ForAuthenticated();
                }

                return EndpointAccessResult.ForPermission(
                    r.OwnPermission,
                    r.AllPermission,
                    r.RequiresMfa);
            }
        }

        if (_opt.FallThroughToAuthenticatedOnly)
        {
            return EndpointAccessResult.ForAuthenticated();
        }

        return EndpointAccessResult.ForPublic();
    }

    private static bool MethodMatches(string? matchExpression, string method)
    {
        if (string.IsNullOrEmpty(matchExpression) || matchExpression is "*")
        {
            return true;
        }

        foreach (var p in matchExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (p is "*")
            {
                return true;
            }

            if (string.Equals(p, method, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPath(PathString path, string? prefix)
    {
        if (string.IsNullOrEmpty(path.Value))
        {
            return false;
        }

        var a = path.Value;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        if (string.Equals(prefix.Trim(), "/", StringComparison.Ordinal))
        {
            return true; // use sparingly: matches all paths
        }

        if (!prefix.StartsWith('/'))
        {
            prefix = "/" + prefix;
        }

        if (!a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (a.Length == prefix.Length)
        {
            return true;
        }

        return a[prefix.Length] is '/';
    }
}
