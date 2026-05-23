using Carter;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Messaging.Idempotency;
using Shared.Cache.DependencyInjection.Extensions;
using UrbanX.Order.Application.DependencyInjection.Extensions;
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;
using UrbanX.Order.Application.Sagas.PlaceOrderSales;
using UrbanX.Order.API.Middleware;
using UrbanX.Order.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Order.Infrastructure.Messaging.OrderCancelledCache;
using UrbanX.Order.Infrastructure.Messaging.OrderConfirmedCache;
using UrbanX.Order.Infrastructure.Sagas.PlaceOrderNormal;
using UrbanX.Order.Infrastructure.Sagas.PlaceOrderSales;
using UrbanX.Order.Persistence;
using UrbanX.Order.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddHttpIdempotency(o => o.ServiceId = "order");
builder.Services.AddOpenApi();

// Database
// Bounded Npgsql pool keeps the sum across services below PostgreSQL max_connections (see AppHost).
// Idle connections release after 60s so background spikes don't permanently hold capacity.
builder.AddNpgsqlDbContext<OrderDbContext>("orderdb",
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

// Infrastructure first — options/consumers/clients/sagas must be registered before MassTransit
// resolves ConsumerDefinition (which inject IOptions<...> bound by AddInfrastructure).
builder.Services.AddInfrastructure();

// Application — MediatR + FluentValidation (single line)
builder.Services.AddApplication();

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

        bus.AddConsumer<OrderConfirmedCacheConsumer>(typeof(OrderConfirmedCacheConsumerDefinition));
        bus.AddConsumer<OrderCancelledCacheConsumer>(typeof(OrderCancelledCacheConsumerDefinition));
    });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>(name: "orderdb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

// Persistence — IUnitOfWork + repositories
builder.Services.AddPersistence();

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
