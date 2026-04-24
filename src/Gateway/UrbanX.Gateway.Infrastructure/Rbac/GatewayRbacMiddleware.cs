using Microsoft.AspNetCore.Http;
using Shared.Security.Gateway;
using UrbanX.Gateway.Application.Abstractions;
using UrbanX.Gateway.Application.Configuration;
using UrbanX.Gateway.Infrastructure.Error;

namespace UrbanX.Gateway.Infrastructure.Rbac;

public sealed class GatewayRbacMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IEndpointAccessRegistry _access;

    public GatewayRbacMiddleware(RequestDelegate next, IEndpointAccessRegistry access)
    {
        _next = next;
        _access = access;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var m = context.Request.Method;
        var p = context.Request.Path;

        var res = _access.GetAccessFor(m, p);
        if (res.Kind is EndpointAccessKind.Public)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.User is not { Identity: { IsAuthenticated: true } })
        {
            await GatewayErrorResponseWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                GatewayErrorCodes.Unauthorized,
                "Authentication required.").ConfigureAwait(false);
            return;
        }

        if (res.Kind is EndpointAccessKind.Authenticated)
        {
            SetPermissionScope(context, "own");
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (res.Kind is EndpointAccessKind.Permission)
        {
            if (res.RequiresMfa && !PermissionClaimReader.MfaSatisfied(context.User))
            {
                await GatewayErrorResponseWriter.WriteAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    GatewayErrorCodes.MfaRequired,
                    "This operation requires verified multi-factor authentication.").ConfigureAwait(false);
                return;
            }

            var perms = PermissionClaimReader.GetPermissionStrings(context.User);
            if (PermissionClaimReader.IsWildcardAdmin(perms))
            {
                SetPermissionScope(context, "all");
                await _next(context).ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrEmpty(res.AllPermission) && Has(perms, res.AllPermission))
            {
                SetPermissionScope(context, "all");
                await _next(context).ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrEmpty(res.OwnPermission) && Has(perms, res.OwnPermission))
            {
                SetPermissionScope(context, "own");
                await _next(context).ConfigureAwait(false);
                return;
            }

            await GatewayErrorResponseWriter.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                GatewayErrorCodes.Forbidden,
                "You are not allowed to call this resource.").ConfigureAwait(false);
            return;
        }

        SetPermissionScope(context, "own");
        await _next(context).ConfigureAwait(false);
    }

    private static void SetPermissionScope(HttpContext context, string scope) =>
        context.Items[GatewayContextItems.PermissionScope] = scope;

    private static bool Has(IReadOnlyList<string> list, string value) =>
        list.Any(v => string.Equals(v, value, StringComparison.Ordinal));
}
