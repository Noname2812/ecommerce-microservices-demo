using Carter;
using Hangfire;
using Hangfire.InMemory;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Cache.DependencyInjection.Extensions;
using Shared.Messaging.Authorization;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Payment.Application.DependencyInjection.Extensions;
using UrbanX.Payment.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Payment.Infrastructure.DependencyInjection.Options;
using UrbanX.Payment.Infrastructure.Jobs;
using UrbanX.Payment.Infrastructure.Messaging.CreatePaymentSession;
using UrbanX.Payment.Infrastructure.Messaging.OrderCancelled;
using UrbanX.Payment.Persistence;
using UrbanX.Payment.Persistence.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSharedCache("redis");
builder.Services.AddOpenApi();

builder.AddNpgsqlDbContext<PaymentDbContext>("paymentdb",
    configureSettings: settings =>
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            MaxPoolSize = 20,
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
            bus.AddEntityFrameworkOutbox<PaymentDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
            });

            bus.AddConsumer<OrderCancelledConsumer>(typeof(OrderCancelledConsumerDefinition));
            bus.AddConsumer<CreatePaymentSessionConsumer>(typeof(CreatePaymentSessionConsumerDefinition));
        });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>(name: "paymentdb", tags: ["ready", "db"]);

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

var sweepJobOptions = app.Services.GetRequiredService<IOptions<PaymentExpirySweepJobOptions>>().Value;
app.Services.GetRequiredService<IRecurringJobManager>()
    .AddOrUpdate<PaymentExpirySweepJob>(
        recurringJobId: "payment-expiry-sweep",
        methodCall: job => job.ExecuteAsync(default),
        cronExpression: sweepJobOptions.CronExpression);

app.MapCarter();
app.Run();

public partial class Program { }
