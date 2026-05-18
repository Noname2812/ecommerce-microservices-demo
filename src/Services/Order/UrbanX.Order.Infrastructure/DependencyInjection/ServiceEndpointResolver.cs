using Microsoft.Extensions.Configuration;

namespace UrbanX.Order.Infrastructure.DependencyInjection;

/// <summary>
/// Resolves outbound service URLs: Aspire-injected <c>services__{name}__*</c> first, then appsettings fallback.
/// </summary>
internal static class ServiceEndpointResolver
{
    public static string Resolve(IConfiguration config, string aspireServiceName, string configuredBaseAddress)
    {
        var aspireUrl = config[$"services__{aspireServiceName}__https__0"]
            ?? config[$"services__{aspireServiceName}__http__0"];

        if (!string.IsNullOrWhiteSpace(aspireUrl))
            return aspireUrl.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(configuredBaseAddress))
            return configuredBaseAddress.Trim().TrimEnd('/');

        throw new InvalidOperationException(
            $"No base address for service '{aspireServiceName}'. " +
            $"Run via AppHost (WithReference) or set Order:{aspireServiceName}Client:BaseAddress.");
    }
}
