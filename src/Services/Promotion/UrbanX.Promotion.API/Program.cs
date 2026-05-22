using Carter;
using Hangfire;
using Hangfire.InMemory;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using StackExchange.Redis;
using UrbanX.Promotion.Application.DependencyInjection.Extensions;
using UrbanX.Promotion.Application.Telemetry;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Options;
using UrbanX.Promotion.Infrastructure.Jobs;
using UrbanX.Promotion.Infrastructure.Messaging.ClaimCouponRequested;
using UrbanX.Promotion.Infrastructure.Messaging.CouponReleaseRequested;
using UrbanX.Promotion.Persistence;
using UrbanX.Promotion.Persistence.DependencyInjection.Extensions;
using UrbanX.Promotion.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(PromotionMetrics.MeterName));
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<PromotionDbContext>("promotiondb",
    configureSettings: settings =>
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            MaxPoolSize = 20,
            MinPoolSize = 2,
            ConnectionIdleLifetime = 60,
            ConnectionPruningInterval = 10,
            Timeout = 15,
            CommandTimeout = 30
        };
        settings.ConnectionString = csb.ConnectionString;
    },
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddInfrastructure();
builder.Services.AddApplication();

builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration,
        configureBus: bus =>
        {
            bus.AddEntityFrameworkOutbox<PromotionDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
            });

            bus.AddConsumer<CouponReleaseRequestedConsumer>(typeof(CouponReleaseRequestedConsumerDefinition));
            bus.AddConsumer<ClaimCouponRequestedConsumer>(typeof(ClaimCouponRequestedConsumerDefinition));
        });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PromotionDbContext>(name: "promotiondb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();
builder.Services.AddPersistence();

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

builder.Services.AddHangfire(config =>
    config.UseInMemoryStorage());
builder.Services.AddHangfireServer();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
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

        if (app.Environment.IsDevelopment())
        {
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            await PromotionDataSeeder.SeedCouponsIfEmptyAsync(context, redis, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
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
