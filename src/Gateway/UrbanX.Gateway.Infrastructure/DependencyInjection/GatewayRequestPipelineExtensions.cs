using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using UrbanX.Gateway.Infrastructure.Correlation;
using UrbanX.Gateway.Infrastructure.Enrichment;
using UrbanX.Gateway.Infrastructure.Observability;
using UrbanX.Gateway.Infrastructure.Rbac;

namespace UrbanX.Gateway.Infrastructure.DependencyInjection;

public static class GatewayRequestPipelineExtensions
{
    /// <summary>
    /// After CORS. Order: correlation, rate limit, authentication, RBAC, header enrichment, structured request logging.
    /// Map reverse proxy after health/OpenAPI and these middlewares.
    /// </summary>
    public static WebApplication UseGatewayDownstreamPipeline(this WebApplication app)
    {
        _ = app.UseMiddleware<GatewayRequestCorrelationMiddleware>();
        _ = app.UseRateLimiter();
        _ = app.UseAuthentication();
        _ = app.UseAuthorization();
        _ = app.UseMiddleware<GatewayRbacMiddleware>();
        _ = app.UseMiddleware<GatewayRequestEnrichmentMiddleware>();
        _ = app.UseMiddleware<GatewayStructuredRequestLoggingMiddleware>();
        return app;
    }
}
