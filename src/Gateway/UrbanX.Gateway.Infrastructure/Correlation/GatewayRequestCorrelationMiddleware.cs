using Microsoft.AspNetCore.Http;
using UrbanX.Gateway.Application.Constants;

namespace UrbanX.Gateway.Infrastructure.Correlation;

public sealed class GatewayRequestCorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public GatewayRequestCorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(GatewayHeaderNames.XRequestId))
        {
            context.Request.Headers[GatewayHeaderNames.XRequestId] = Guid.NewGuid().ToString("D");
        }

        await _next(context).ConfigureAwait(false);
    }
}
