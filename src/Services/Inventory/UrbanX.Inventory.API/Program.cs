using Carter;
using Hangfire;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.DependencyInjection.Extensions;
using UrbanX.Inventory.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Inventory.Infrastructure.DependencyInjection.Options;
using UrbanX.Inventory.Infrastructure.Jobs;
using UrbanX.Inventory.Infrastructure.Messaging.ConfirmInventoryRequested;
using UrbanX.Inventory.Infrastructure.Messaging.InventoryReleaseRequested;
using UrbanX.Inventory.Infrastructure.Messaging.ReserveInventoryRequested;
using UrbanX.Inventory.Persistence;
using UrbanX.Inventory.Persistence.DependencyInjection.Extensions;
using UrbanX.Inventory.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<InventoryDbContext>("inventorydb",
    configureSettings: settings =>
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            MaxPoolSize = 40,
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
    .AddMessaging(
        builder.Configuration,
        configureBus: bus =>
        {
            bus.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
            });

            bus.AddConsumer<InventoryReleaseRequestedConsumer>(typeof(InventoryReleaseRequestedConsumerDefinition));
            bus.AddConsumer<ReserveInventoryRequestedConsumer>(typeof(ReserveInventoryRequestedConsumerDefinition));
            bus.AddConsumer<ConfirmInventoryRequestedConsumer>(typeof(ConfirmInventoryRequestedConsumerDefinition));
        });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>(name: "inventorydb", tags: ["ready", "db"]);

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

builder.Services.AddHangfire(config => config.UseInMemoryStorage());
builder.Services.AddHangfireServer();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
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

var jobOptions = app.Services.GetRequiredService<IOptions<ReleaseExpiredReservationsJobOptions>>().Value;
app.Services.GetRequiredService<IRecurringJobManager>()
    .AddOrUpdate<ReleaseExpiredReservationsJob>(
        recurringJobId: "ttl-release-expired-reservations",
        methodCall: job => job.ExecuteAsync(),
        cronExpression: jobOptions.CronExpression);

app.MapCarter();
app.Run();

public partial class Program { }
