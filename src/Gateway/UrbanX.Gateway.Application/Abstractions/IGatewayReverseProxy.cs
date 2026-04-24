using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UrbanX.Gateway.Application.Abstractions;

/// <summary>
/// YARP reverse proxy registration and endpoint mapping. Implemented in infrastructure; injectable for tests.
/// </summary>
public interface IGatewayReverseProxy
{
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);
}
