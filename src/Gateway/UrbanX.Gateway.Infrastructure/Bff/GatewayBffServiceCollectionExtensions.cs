using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using UrbanX.Gateway.Application.Configuration;

namespace UrbanX.Gateway.Infrastructure.Bff;

public static class GatewayBffServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayBff(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<GatewayBffOptions>()
            .Bind(configuration.GetSection(GatewayBffOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var bff = configuration.GetSection(GatewayBffOptions.SectionName).Get<GatewayBffOptions>() ?? new GatewayBffOptions();
        var authority = IdentityAuthorityResolver.Resolve(configuration)
            ?? throw new InvalidOperationException("Identity authority not configured. Set IdentityServer:Authority or services__identity__*.");

        services.AddBff()
            .AddServerSideSessions();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = bff.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(bff.SessionLifetimeMinutes);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api") ||
                        ctx.Request.Path.StartsWithSegments("/bff"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;
                options.ClientId = bff.ClientId;
                options.ClientSecret = bff.ClientSecret;
                options.ResponseType = "code";
                options.ResponseMode = "query";
                options.UsePkce = true;

                options.CallbackPath = bff.SignInPath;
                options.SignedOutCallbackPath = bff.SignOutCallbackPath;
                options.RemoteSignOutPath = bff.RemoteSignOutPath;

                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;
                options.SaveTokens = true;

                options.RequireHttpsMetadata = !environment.IsDevelopment()
                    && !authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

                options.Scope.Clear();
                foreach (var scope in bff.Scopes)
                {
                    options.Scope.Add(scope);
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        return services;
    }
}
