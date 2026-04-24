using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UrbanX.Gateway.Application.Abstractions;
using UrbanX.Gateway.Application.Configuration;
using UrbanX.Gateway.Infrastructure.Edge;
using UrbanX.Gateway.Infrastructure.Enrichment;
using UrbanX.Gateway.Infrastructure.Rbac;
using UrbanX.Gateway.Infrastructure.ReverseProxy;

namespace UrbanX.Gateway.Infrastructure.DependencyInjection;

public static class GatewayInfrastructureServiceCollectionExtensions
{
    /// <summary>Registers CORS, Kestrel edge TLS, JWT, rate limits, RBAC, enrichment, logging, and YARP.</summary>
    public static IServiceCollection AddGatewayInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetSection(CorsEdgeOptions.SectionName).GetChildren().Any()
            && string.IsNullOrEmpty(configuration["Cors:AllowedOrigins:0"])
            && string.IsNullOrEmpty(configuration["Cors:AllowedOrigins"]))
        {
            throw new InvalidOperationException("Missing or empty configuration section: Cors (edge CORS contract).");
        }

        services.AddGatewayCors(configuration);
        services.AddSingleton<IKestrelEdgeTlsConfiguration, KestrelEdgeTlsConfiguration>();

        services.Configure<GatewayRbacOptions>(configuration.GetSection(GatewayRbacOptions.SectionName));
        services.AddSingleton<IConfigureOptions<GatewayRbacOptions>, GatewayRbacOptionsSetup>();
        services.AddSingleton<IEndpointAccessRegistry, EndpointAccessRegistry>();
        services.AddSingleton<IRequestHeaderEnricher, RequestHeaderEnricher>();

        services.AddGatewayRateLimiting(configuration);
        _ = services.AddGatewayAuthentication(configuration, environment);

        var reverseProxy = new YarpGatewayReverseProxy();
        reverseProxy.RegisterServices(services, configuration);
        services.AddSingleton<IGatewayReverseProxy>(reverseProxy);

        return services;
    }

    public static void UseGatewayEdgeCors(this WebApplication app) =>
        app.UseCors(EdgeCorsPolicyNames.Default);

    public static IEndpointRouteBuilder MapGatewayReverseProxy(this WebApplication app)
    {
        app.Services.GetRequiredService<IGatewayReverseProxy>().MapEndpoints(app);
        return app;
    }
}
