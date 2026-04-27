using Duende.Bff.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrbanX.Gateway.Application.Abstractions;

namespace UrbanX.Gateway.Infrastructure.ReverseProxy;

public sealed class YarpGatewayReverseProxy : IGatewayReverseProxy
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var reverse = configuration.GetSection("ReverseProxy");
        if (!configuration.GetSection("ReverseProxy:Routes").GetChildren().Any())
        {
            throw new InvalidOperationException("Missing or empty configuration section: ReverseProxy:Routes");
        }

        services.AddReverseProxy()
            .AddBffExtensions()
            .LoadFromConfig(reverse);
    }

    public void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapReverseProxy(proxy => proxy.UseAntiforgeryCheck());
}
