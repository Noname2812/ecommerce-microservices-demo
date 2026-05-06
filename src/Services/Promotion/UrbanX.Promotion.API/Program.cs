using Carter;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using StackExchange.Redis;
using UrbanX.Promotion.API.SeedData;
using UrbanX.Promotion.API.Messaging;
using UrbanX.Promotion.Application.DependencyInjection.Extensions;
using UrbanX.Promotion.Application.Jobs;
using UrbanX.Promotion.Application.Messaging;
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

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PromotionDbContext>(name: "promotiondb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

builder.Services.AddPersistence();
builder.Services.AddPromotionInfrastructure();
builder.Services.AddApplication(builder.Configuration);

builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration,
        configureBus: bus =>
        {
            bus.AddConsumer<CouponReleaseRequestedConsumer>(typeof(CouponReleaseRequestedConsumerDefinition));
        });

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

// In-memory storage is fine for local/dev. For production, switch to persistent storage (e.g. Hangfire.PostgreSql/Redis).
builder.Services.AddHangfire(config =>
    config.UseInMemoryStorage());
builder.Services.AddHangfireServer();

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

var ttlJobOptions = app.Services.GetRequiredService<IOptions<ReleaseExpiredCouponClaimsJobOptions>>().Value;
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobManager.AddOrUpdate<ReleaseExpiredCouponClaimsJob>(
    recurringJobId: "ttl-release-expired-coupon-claims",
    methodCall: job => job.ExecuteAsync(default),
    cronExpression: ttlJobOptions.CronExpression);

app.MapCarter();
app.Run();

public partial class Program { }
