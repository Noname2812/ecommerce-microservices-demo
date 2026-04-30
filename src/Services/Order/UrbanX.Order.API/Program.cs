using Carter;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using Shared.Outbox.DependencyInjection.Extensions;
using UrbanX.Order.Application.DependencyInjection.Extensions;
using UrbanX.Order.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

// Database
builder.AddNpgsqlDbContext<OrderDbContext>("orderdb");
builder.Services.AddOutbox<OrderDbContext>(
    configureDb: null,
    builder.Configuration
);

// Messaging
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>(name: "orderdb", tags: ["ready", "db"]);

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

app.UseUserContext();

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
