using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace UrbanX.Gateway.Application.Abstractions;

/// <summary>
/// In-process Kestrel TLS (edge) — optional; often TLS terminates at a load balancer instead.
/// CORS is registered separately (Shared.Security <c>AddUrbanXEdgeCors</c>).
/// </summary>
public interface IKestrelEdgeTlsConfiguration
{
    void Apply(KestrelServerOptions kestrel, IConfiguration configuration, IHostEnvironment environment);
}
