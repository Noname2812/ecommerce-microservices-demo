using Carter;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using StackExchange.Redis;
using UrbanX.Promotion.API.SeedData;
using UrbanX.Promotion.Application.DependencyInjection.Extensions;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Promotion.Persistence;
using UrbanX.Promotion.Application.Telemetry;
using UrbanX.Promotion.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(PromotionMetrics.MeterName));
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<PromotionDbContext>("promotiondb");
builder.Services.AddOutbox<PromotionDbContext>(
    configureDb: null,
    builder.Configuration
);

builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PromotionDbContext>(name: "promotiondb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

builder.Services.AddPersistence();
builder.Services.AddPromotionInfrastructure();
builder.Services.AddApplication(builder.Configuration);

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();

// Trust-the-Gateway: read identity from X-User-* headers (set by Gateway).
// Authorization is enforced via AuthorizationPipelineBehavior on each Command/Query.
app.UseUserContext();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PromotionDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for PromotionDbContext...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }

    if (app.Environment.IsDevelopment())
    {
        var db = scope.ServiceProvider.GetRequiredService<PromotionDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        await PromotionDbContextSeed.SeedCouponsAsync(db, redis, logger);
    }
}

app.MapCarter();
app.Run();

public partial class Program { }
