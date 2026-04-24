using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Shared.Security.Edge;

/// <summary>
/// Registers the gateway edge CORS policy as <see cref="EdgeCorsPolicyNames.Default"/> using
/// <see cref="CorsEdgeOptions"/> (section <see cref="CorsEdgeOptions.SectionName"/>).
/// </summary>
public static class UrbanXEdgeCorsServiceCollectionExtensions
{
    public static IServiceCollection AddUrbanXEdgeCors(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CorsEdgeOptions>()
            .BindConfiguration(CorsEdgeOptions.SectionName);

        services.AddSingleton<IValidateOptions<CorsEdgeOptions>, CorsEdgeOptionsValidation>();
        services.AddCors();
        services.AddSingleton<IConfigureOptions<CorsOptions>, UrbanXEdgeCorsOptionsSetup>();

        return services;
    }
}

file sealed class UrbanXEdgeCorsOptionsSetup(IOptions<CorsEdgeOptions> edge) : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        var e = edge.Value;

        options.AddPolicy(EdgeCorsPolicyNames.Default, policy =>
        {
            policy
                .WithOrigins(e.AllowedOrigins)
                .WithMethods(e.AllowedMethods)
                .WithHeaders(e.AllowedHeaders);

            if (e.ExposedHeaders is { Length: > 0 })
            {
                policy.WithExposedHeaders(e.ExposedHeaders);
            }

            if (e.AllowCredentials)
            {
                policy.AllowCredentials();
            }
            else
            {
                policy.DisallowCredentials();
            }

            policy.SetPreflightMaxAge(TimeSpan.FromSeconds(e.PreflightMaxAgeSeconds));
        });
    }
}

file sealed class CorsEdgeOptionsValidation : IValidateOptions<CorsEdgeOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsEdgeOptions options)
    {
        if (options.AllowedOrigins is not { Length: > 0 })
        {
            return ValidateOptionsResult.Fail("Cors:AllowedOrigins must contain at least one origin.");
        }

        if (options.AllowCredentials
            && options.AllowedOrigins.Any(static o => o is "*"))
        {
            return ValidateOptionsResult.Fail("Wildcard origin is not valid when AllowCredentials is true.");
        }

        return ValidateOptionsResult.Success;
    }
}
