using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.DependencyInjection.Options;
using UrbanX.Inventory.Application.Jobs;
using UrbanX.Inventory.Application.Messaging;
using UrbanX.Inventory.Persistence;
using UrbanX.Inventory.Persistence.DependencyInjection.Extensions;
using UrbanX.Inventory.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<InventoryDbContext>("inventorydb",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
builder.Services.AddOutbox<InventoryDbContext>(
    configureDb: null,
    builder.Configuration
);

// Application (options for ConsumerDefinition, MediatR, …) — register before MassTransit resolves definitions at startup
builder.Services.AddApplication();

// Messaging — compensation.events fanout (see Shared.Outbox CompensationOutboxRelayWorker)
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(
        builder.Configuration,
        configureBus: bus =>
        {
            bus.AddConsumer<InventoryReleaseRequestedConsumer>(typeof(InventoryReleaseRequestedConsumerDefinition));
        });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>(name: "inventorydb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Add Persistence
builder.Services.AddPersistence();

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddCarter();

// Hangfire — in-memory storage (swap to Hangfire.PostgreSql for production)
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
    var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for InventoryDbContext...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
        await InventoryDataSeeder.SeedIfEmptyAsync(context);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}

// Schedule TTL job using configured cron expression
var jobOptions = app.Services.GetRequiredService<IOptions<ReleaseExpiredReservationsJobOptions>>().Value;
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobManager.AddOrUpdate<ReleaseExpiredReservationsJob>(
    recurringJobId: "ttl-release-expired-reservations",
    methodCall: job => job.ExecuteAsync(),
    cronExpression: jobOptions.CronExpression);

app.MapCarter();
app.Run();

public partial class Program { }
