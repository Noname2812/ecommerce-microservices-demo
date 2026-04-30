using Carter;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Catalog.Application.DependencyInjection.Extensions;
using UrbanX.Catalog.Persistence;
using UrbanX.Catalog.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components
builder.AddServiceDefaults();
builder.AddSharedCache("redis");

// Add services to the container.
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");
builder.Services.AddOutbox<CatalogDbContext>(
    configureDb: null,
    builder.Configuration
);

// Add Message queue
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging();

// Add database health check
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>(name: "catalogdb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Add infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// Add Persistence
builder.Services.AddPersistence();

// Add Application
builder.Services.AddApplication(builder.Configuration);

// Add versioning
builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// Add Carter
builder.Services.AddCarter();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52428800; // Set file size limit (e.g., 50 MB)
});

var app = builder.Build();

// Map default endpoints (health checks, etc.)
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

// Trust-the-Gateway: read identity from X-User-* headers (set by Gateway).
// Authorization is enforced via AuthorizationPipelineBehavior on each Command/Query.
app.UseUserContext();

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations for CatalogDbContext...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}
app.MapCarter();
app.Run();

// Make the implicit Program class public so integration tests can reference it
public partial class Program { }
