using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrbanX.Gateway.Application.Constants;
using UrbanX.Gateway.Application.Configuration;
using UrbanX.Gateway.Infrastructure.Error;

namespace UrbanX.Gateway.Infrastructure.DependencyInjection;

public static class GatewayRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var rate = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var segs = Math.Max(1, rate.SlidingWindowSegments);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, token) =>
            {
                if (ctx.HttpContext.Response.HasStarted)
                {
                    return;
                }

                var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var t)
                    ? (int)Math.Ceiling(t.TotalSeconds)
                    : 0;

                await GatewayErrorResponseWriter.WriteAsync(
                    ctx.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    GatewayErrorCodes.RateLimitExceeded,
                    "Too many requests. Please try again after a short time.",
                    retryAfter: retry > 0 ? retry : null,
                    includeRateLimitBodyFields: true,
                    cancellationToken: token).ConfigureAwait(false);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var p = httpContext.Request.Path;
                if (p.StartsWithSegments(new PathString("/health"), StringComparison.OrdinalIgnoreCase, out var _)
                    || p.StartsWithSegments(new PathString("/alive"), StringComparison.OrdinalIgnoreCase, out var _))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        $"health:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            SegmentsPerWindow = segs,
                            PermitLimit = 100_000,
                            Window = TimeSpan.FromSeconds(60),
                            QueueLimit = 0
                        });
                }

                if (IsAuthTight(p))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        $"auth:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            SegmentsPerWindow = segs,
                            PermitLimit = rate.AuthEndpoints.PermitLimit,
                            Window = TimeSpan.FromSeconds(rate.AuthEndpoints.WindowSeconds),
                            QueueLimit = 0
                        });
                }

                if (IsWriteMethod(httpContext.Request.Method))
                {
                    var wKey = "write:" + GetUserIdOrIp(httpContext);
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        wKey,
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            SegmentsPerWindow = segs,
                            PermitLimit = rate.WriteOperations.PermitLimit,
                            Window = TimeSpan.FromSeconds(rate.WriteOperations.WindowSeconds),
                            QueueLimit = 0
                        });
                }

                if (httpContext.User.Identity is { IsAuthenticated: true })
                {
                    var s = httpContext.User.FindFirst("sub")?.Value
                        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? "u";
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        "user:" + s,
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            SegmentsPerWindow = segs,
                            PermitLimit = rate.AuthenticatedUser.PermitLimit,
                            Window = TimeSpan.FromSeconds(rate.AuthenticatedUser.WindowSeconds),
                            QueueLimit = 0
                        });
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    $"global:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        SegmentsPerWindow = segs,
                        PermitLimit = rate.GlobalPerIp.PermitLimit,
                        Window = TimeSpan.FromSeconds(rate.GlobalPerIp.WindowSeconds),
                        QueueLimit = 0
                    });
            });
        });
    }

    private static string GetUserIdOrIp(HttpContext c) =>
        c.User.Identity is { IsAuthenticated: true }
            ? (c.User.FindFirst("sub")?.Value
               ?? c.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? c.Connection.RemoteIpAddress?.ToString() ?? c.Connection.Id)
            : c.Connection.RemoteIpAddress?.ToString() ?? c.Connection.Id;

    private static bool IsWriteMethod(string m) => m is "POST" or "PUT" or "PATCH" or "DELETE";

    private static bool IsAuthTight(PathString p) =>
        p.StartsWithSegments(new PathString("/api/account"), StringComparison.OrdinalIgnoreCase, out var _)
        || p.StartsWithSegments(new PathString("/connect"), StringComparison.OrdinalIgnoreCase, out var _);
}
