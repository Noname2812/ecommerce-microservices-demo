using Carter;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.DependencyInjection.Extensions;
using UrbanX.Inventory.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<InventoryDbContext>("inventorydb");
builder.Services.AddOutbox<InventoryDbContext>(
    configureDb: null,
    builder.Configuration
);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>(name: "inventorydb", tags: ["ready", "db"]);

builder.Services.AddProblemDetails();
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
    var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations for InventoryDbContext...");
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
