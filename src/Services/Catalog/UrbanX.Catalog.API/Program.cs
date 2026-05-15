using Carter;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Catalog.API.Exceptions;
using UrbanX.Catalog.Application.DependencyInjection.Extensions;
using UrbanX.Catalog.Application.Messaging;
using UrbanX.Catalog.Persistence;
using UrbanX.Catalog.Persistence.DependencyInjection.Extensions;
using UrbanX.Catalog.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components
builder.AddServiceDefaults();
builder.AddSharedCache("redis");

// Add services to the container.
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("catalogdb")!;

// Write pool: EF Core + Outbox relay + projection consumers.
// Kept small so reads never starve writes and vice versa.
builder.Services.AddNpgsqlDataSource(connectionString, ds =>
{
    ds.ConnectionStringBuilder.MaxPoolSize      = 50;
    ds.ConnectionStringBuilder.MinPoolSize      = 5;
    ds.ConnectionStringBuilder.CommandTimeout   = 7;   // must be < request timeout (8 s)
    ds.ConnectionStringBuilder.ApplicationName  = "catalog-write";
});
builder.Services.AddDbContextPool<CatalogDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
           .UseSnakeCaseNamingConvention());

// Read pool: Dapper (ProductReadModelRepository) — high-concurrency reads only.
// Registered as keyed singleton so the write pool above remains the default NpgsqlDataSource.
builder.Services.AddKeyedSingleton<NpgsqlDataSource>("catalog-read", (_, _) =>
    NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize     = 150,
        MinPoolSize     = 10,
        CommandTimeout  = 7,
        ApplicationName = "catalog-read",
    }.ConnectionString));

builder.Services.AddOutbox<CatalogDbContext>(
    configureDb: null,
    builder.Configuration
);

// Add Message queue
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddConsumer<ProductCreatedProjectionConsumer>();
        bus.AddConsumer<ProductInfoUpdatedProjectionConsumer>();
        bus.AddConsumer<ProductStatusChangedProjectionConsumer>();
        bus.AddConsumer<ProductVariantAddedProjectionConsumer>();
        bus.AddConsumer<ProductVariantUpdatedProjectionConsumer>();
        bus.AddConsumer<ProductVariantDeletedProjectionConsumer>();
    });

// Add database health check
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>(name: "catalogdb", tags: ["ready", "db"]);

builder.Services.AddExceptionHandler<OperationCancelledExceptionHandler>();
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
        if (app.Environment.IsDevelopment())
        {
            logger.LogInformation("Seeding catalog data...");
            await CatalogDataSeeder.SeedIfEmptyAsync(context);
            logger.LogInformation("Catalog data seeded successfully");
        }
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
