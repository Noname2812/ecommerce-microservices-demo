using Carter;
using Duende.IdentityServer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Identity.API.Configuration;
using UrbanX.Identity.Application.DependencyInjection.Extensions;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Identity.Persistence;
using UrbanX.Identity.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

// Database + Outbox
builder.AddNpgsqlDbContext<IdentityDbContext>("identitydb");
builder.Services.AddOutbox<IdentityDbContext>(
    configureDb: null,
    builder.Configuration);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>(name: "identitydb", tags: ["ready", "db"]);

// ASP.NET Core Identity
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;

        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = builder.Configuration.GetValue("Identity:Lockout:MaxFailedAccessAttempts", 5);
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("Identity:Lockout:DefaultLockoutMinutes", 15));
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddDefaultTokenProviders();

// Cookie config (used during external login challenge → callback, and Quickstart login UI)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "urbanx.identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Duende IdentityServer
var identityServerBuilder = builder.Services.AddIdentityServer(options =>
    {
        options.Events.RaiseErrorEvents = true;
        options.Events.RaiseInformationEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseSuccessEvents = true;
        options.EmitStaticAudienceClaim = true;
    })
    .AddInMemoryIdentityResources(IdentityServerResources.IdentityResources)
    .AddInMemoryApiScopes(IdentityServerResources.ApiScopes)
    .AddInMemoryApiResources(IdentityServerResources.ApiResources)
    .AddInMemoryClients(IdentityServerResources.Clients(builder.Configuration))
    .AddAspNetIdentity<ApplicationUser>();

if (builder.Environment.IsDevelopment())
{
    identityServerBuilder.AddDeveloperSigningCredential();
}

// External authentication providers
var authenticationBuilder = builder.Services.AddAuthentication();

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
authenticationBuilder.AddGoogle(options =>
{
    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
    options.ClientId = googleClientId;
    options.ClientSecret = googleClientSecret;
    options.CallbackPath = "/signin-google";
});
}

builder.Services.AddProblemDetails();

// Add Infrastructure + Persistence
builder.Services.AddIdentityInfrastructure();
builder.Services.AddPersistence();

// Add Application
builder.Services.AddApplication(builder.Configuration);

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();

// Trust-the-Gateway for downstream management endpoints (/api/v1/identity/**)
// Identity also exposes its own auth via Duende (/connect/**, /api/account/**)
app.UseUserContext();

app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();

// Auto-migrate + seed on startup
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var context = sp.GetRequiredService<IdentityDbContext>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for IdentityDbContext...");
        await context.Database.MigrateAsync();

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        await IdentitySeeder.SeedAsync(userManager, roleManager, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing IdentityDbContext");
        throw;
    }
}

app.MapCarter();
app.MapDefaultControllerRoute();
app.Run();

public partial class Program { }
