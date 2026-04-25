using Microsoft.AspNetCore.Http;
using Shared.Kernel.Constants;
using System.Diagnostics;

namespace Shared.Messaging.Authorization;

public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            if (context.Request.Headers.TryGetValue(GatewayHeaderNames.XUserId, out var uid)
                && !string.IsNullOrWhiteSpace(uid))
            {
                activity.SetTag("user.id", uid.ToString());
            }

            if (context.Request.Headers.TryGetValue(GatewayHeaderNames.XRequestId, out var rid)
                && !string.IsNullOrWhiteSpace(rid))
            {
                activity.SetTag("request.id", rid.ToString());
            }

            if (context.Request.Headers.TryGetValue(GatewayHeaderNames.XMerchantId, out var mid)
                && !string.IsNullOrWhiteSpace(mid))
            {
                activity.SetTag("merchant.id", mid.ToString());
            }
        }

        await _next(context);
    }
}
