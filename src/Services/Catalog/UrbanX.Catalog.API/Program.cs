using Carter;
using MassTransit;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Catalog.API.Exceptions;
using UrbanX.Catalog.Application.DependencyInjection.Extensions;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Catalog.Infrastructure.Messaging.ProductCreated;
using UrbanX.Catalog.Infrastructure.Messaging.ProductInfoUpdated;
using UrbanX.Catalog.Infrastructure.Messaging.ProductStatusChanged;
using UrbanX.Catalog.Infrastructure.Messaging.ProductVariantAdded;
using UrbanX.Catalog.Infrastructure.Messaging.ProductVariantDeleted;
using UrbanX.Catalog.Infrastructure.Messaging.ProductVariantUpdated;
using UrbanX.Catalog.Persistence;
using UrbanX.Catalog.Persistence.DependencyInjection.Extensions;
using UrbanX.Catalog.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource(ProductReadModelRepository.ActivitySourceName)
        .AddSource("SharedKernel.Cache"))
    .WithMetrics(m => m
        .AddMeter(ProductReadModelRepository.MeterName)
        .AddMeter("SharedKernel.Cache"));

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("catalogdb")!;

builder.Services.AddNpgsqlDataSource(connectionString, ds =>
{
    ds.ConnectionStringBuilder.MaxPoolSize = 50;
    ds.ConnectionStringBuilder.MinPoolSize = 5;
    ds.ConnectionStringBuilder.CommandTimeout = 7;
    ds.ConnectionStringBuilder.ApplicationName = "catalog-write";
});
builder.Services.AddDbContextPool<CatalogDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
           .UseSnakeCaseNamingConvention());

builder.Services.AddKeyedSingleton<NpgsqlDataSource>("catalog-read", (_, _) =>
    NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = 150,
        MinPoolSize = 10,
        CommandTimeout = 7,
        ApplicationName = "catalog-read",
    }.ConnectionString));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddEntityFrameworkOutbox<CatalogDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
        });

        bus.AddConsumer<ProductCreatedProjectionConsumer>(typeof(ProductCreatedProjectionConsumerDefinition));
        bus.AddConsumer<ProductInfoUpdatedProjectionConsumer>(typeof(ProductInfoUpdatedProjectionConsumerDefinition));
        bus.AddConsumer<ProductStatusChangedProjectionConsumer>(typeof(ProductStatusChangedProjectionConsumerDefinition));
        bus.AddConsumer<ProductVariantAddedProjectionConsumer>(typeof(ProductVariantAddedProjectionConsumerDefinition));
        bus.AddConsumer<ProductVariantUpdatedProjectionConsumer>(typeof(ProductVariantUpdatedProjectionConsumerDefinition));
        bus.AddConsumer<ProductVariantDeletedProjectionConsumer>(typeof(ProductVariantDeletedProjectionConsumerDefinition));
    });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>(name: "catalogdb", tags: ["ready", "db"]);

builder.Services.AddExceptionHandler<OperationCancelledExceptionHandler>();
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
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52428800;
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
app.UseUserContext();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations for CatalogDbContext...");
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
        if (app.Environment.IsDevelopment())
        {
            logger.LogInformation("Seeding catalog data...");
            await CatalogDataSeeder.SeedIfEmptyAsync(context);
            logger.LogInformation("Catalog data seeded successfully");
        }
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
