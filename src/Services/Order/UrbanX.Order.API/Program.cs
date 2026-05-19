using Carter;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Messaging.Idempotency;
using Shared.Cache.DependencyInjection.Extensions;
using UrbanX.Order.Application.DependencyInjection.Extensions;
using UrbanX.Order.Application.Messaging;
using UrbanX.Order.API.Middleware;
using UrbanX.Order.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Order.Persistence;
using UrbanX.Order.Persistence.DependencyInjection.Extensions;
using UrbanX.Order.Application.Sagas.PlaceOrderSales;
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddHttpIdempotency(o => o.ServiceId = "order");
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<OrderDbContext>("orderdb", 
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());
// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddEntityFrameworkOutbox<OrderDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
        });

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

        bus.AddConsumer<OrderConfirmedCacheConsumer>();
        bus.AddConsumer<OrderCancelledCacheConsumer>();
    });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>(name: "orderdb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Add Infrastructure
builder.Services.AddInfrastructure();

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
