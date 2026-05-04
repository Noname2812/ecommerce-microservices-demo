using Carter;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using Shared.Cache.DependencyInjection.Extensions;
using UrbanX.Payment.Application.DependencyInjection.Extensions;
using UrbanX.Payment.Application.Messaging;
using UrbanX.Payment.Persistence;
using UrbanX.Payment.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<PaymentDbContext>("paymentdb");
builder.Services.AddOutbox<PaymentDbContext>(
    configureDb: null,
    builder.Configuration
);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(
        builder.Configuration,
        configureBus: bus =>
        {
            bus.AddConsumer<OrderCreatedConsumer>();
            bus.AddConsumer<OrderCancelledConsumer>();
        });

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>(name: "paymentdb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();

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

// Trust-the-Gateway: read identity from X-User-* headers (set by Gateway).
// Authorization is enforced via AuthorizationPipelineBehavior on each Command/Query.
app.UseUserContext();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for PaymentDbContext...");
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
