using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Shared.Security.Gateway;
using UrbanX.Gateway.Application.Configuration;

namespace UrbanX.Gateway.Infrastructure.DependencyInjection;

public static class GatewayAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<GatewayJwtOptions>()
            .Bind(configuration.GetSection(GatewayJwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(GatewayJwtOptions.SectionName).Get<GatewayJwtOptions>() ?? new();
        var authority = ResolveAuthority(configuration) ?? options.Authority;

        if (string.IsNullOrEmpty(authority))
        {
            throw new InvalidOperationException("JWT authority is not configured. Set Jwt:Authority or services__identity__* or IdentityServer:Authority.");
        }

        var audience = options.Audience ?? configuration["IdentityServer:Audience"] ?? "urbanx-api";
        if (string.IsNullOrEmpty(audience))
        {
            throw new InvalidOperationException("JWT audience is not configured (Jwt:Audience or IdentityServer:Audience).");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience = audience;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ValidAudience = audience,
                    ValidIssuer = authority
                };
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds);
                o.RequireHttpsMetadata = !environment.IsDevelopment() && !authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = c =>
                    {
                        if (string.IsNullOrEmpty(c.Token) && c.Request.Cookies.ContainsKey("access_token"))
                        {
                            c.Token = c.Request.Cookies["access_token"];
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async c =>
                    {
                        c.HandleResponse();
                        c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        c.Response.ContentType = "application/json; charset=utf-8";
                        var id = c.Request.Headers[GatewayHeaderNames.XRequestId].ToString();
                        if (string.IsNullOrEmpty(id))
                        {
                            id = c.HttpContext.TraceIdentifier;
                        }

                        if (!c.Response.HasStarted)
                        {
                            c.Response.Headers[GatewayHeaderNames.XRequestId] = id;
                        }

                        var ct = c.HttpContext.RequestAborted;
                        await c.Response.WriteAsync(
                            JsonSerializer.Serialize(new
                            {
                                request_id = id,
                                timestamp = DateTimeOffset.UtcNow.ToString("o"),
                                error = GatewayErrorCodes.Unauthorized,
                                message = "The access token is missing or not valid for this resource.",
                                details = (string?)null
                            }),
                            ct).ConfigureAwait(false);
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static string? ResolveAuthority(IConfiguration config) =>
        (config["services__identity__https__0"]
         ?? config["services__identity__http__0"]
         ?? config["IdentityServer:Authority"]
         ?? config["Jwt:Authority"])?
            .Trim()
            .TrimEnd('/');
}
