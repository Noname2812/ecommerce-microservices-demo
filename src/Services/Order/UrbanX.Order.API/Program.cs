using Carter;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Messaging.Idempotency;
using Shared.Outbox.DependencyInjection.Extensions;
using Shared.Cache.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using UrbanX.Order.Application.Constants;
using UrbanX.Order.Application.DependencyInjection.Extensions;
using UrbanX.Order.Application.Messaging.Catalog;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.API.Middleware;
using UrbanX.Order.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Order.Persistence;
using UrbanX.Order.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");

// Register Order-specific meter for OpenTelemetry export
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddMeter(CatalogProjectionConstants.Metrics.MeterName));
builder.Services.AddHttpIdempotency(o => o.ServiceId = "order");
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<OrderDbContext>("orderdb", 
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
builder.Services.AddOutbox<OrderDbContext>(
    configureDb: null,
    builder.Configuration
);
builder.Services.AddCompensationOutbox(builder.Configuration);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddSagaStateMachine<PlaceSalesOrderSagaStateMachine, PlaceSalesOrderSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<OrderDbContext>();
            });

        bus.AddSagaStateMachine<PlaceOrderNormalSagaStateMachine, PlaceOrderNormalSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<OrderDbContext>();
            });

        // Catalog projection consumers — maintain read.catalog_snapshots for validators
        bus.AddConsumer<ProductCreatedConsumer>();
        bus.AddConsumer<ProductInfoUpdatedConsumer>();
        bus.AddConsumer<ProductStatusChangedConsumer>();
        bus.AddConsumer<ProductVariantAddedConsumer>();
        bus.AddConsumer<ProductVariantUpdatedConsumer>();
        bus.AddConsumer<ProductVariantDeletedConsumer>();
    });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>(name: "orderdb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Add Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// Add Persistence
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

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();

app.UseHttpIdempotency();
app.UseUserContext();
app.UseMiddleware<PlaceOrderRateLimitMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for OrderDbContext...");
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

public partial class Program { }
