using Microsoft.Extensions.Configuration;

namespace UrbanX.Gateway.Infrastructure.Bff;

internal static class IdentityAuthorityResolver
{
    public static string? Resolve(IConfiguration config) =>
        (config["services__identity__https__0"]
         ?? config["services__identity__http__0"]
         ?? config["IdentityServer:Authority"])?
            .Trim()
            .TrimEnd('/');
}
