using Carter;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

// JWT auth
var identityAuthority = builder.Configuration["services__identity__https__0"]
    ?? builder.Configuration["services__identity__http__0"]
    ?? builder.Configuration["IdentityServer:Authority"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityAuthority;
        options.Audience = builder.Configuration["IdentityServer:Audience"] ?? "urbanx-api";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
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
app.UseAuthentication();
app.UseAuthorization();

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
