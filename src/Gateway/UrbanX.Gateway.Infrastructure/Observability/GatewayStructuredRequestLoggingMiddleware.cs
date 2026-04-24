using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace UrbanX.Gateway.Infrastructure.Observability;

public sealed class GatewayStructuredRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayStructuredRequestLoggingMiddleware> _log;

    public GatewayStructuredRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<GatewayStructuredRequestLoggingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var path = context.Request.Path;
        var method = context.Request.Method;
        var traceId = Activity.Current?.Id ?? string.Empty;
        var userId = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
        }

        var sc = context.Response?.StatusCode ?? 0;
        if (!ShouldLogHeaders(context, path))
        {
            return;
        }

        if (sc is >= 400 and < 500)
        {
            _log.LogWarning(
                "Gateway {Method} {Path} -> {StatusCode} in {DurationMs} ms, trace {TraceId}, sub {UserId}",
                method, path, sc, sw.ElapsedMilliseconds, traceId, string.IsNullOrEmpty(userId) ? "-" : userId);
        }
        else
        {
            _log.LogInformation(
                "Gateway {Method} {Path} -> {StatusCode} in {DurationMs} ms, trace {TraceId}, sub {UserId}",
                method, path, sc, sw.ElapsedMilliseconds, traceId, string.IsNullOrEmpty(userId) ? "-" : userId);
        }
    }

    private static bool ShouldLogHeaders(HttpContext context, PathString path)
    {
        if (path.HasValue
            && (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/alive", StringComparison.OrdinalIgnoreCase)))
        {
            return false; // high churn; k8s probes
        }

        return true;
    }
}
