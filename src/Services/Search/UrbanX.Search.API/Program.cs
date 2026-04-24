using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Search.Application.DependencyInjection.Extensions;
using UrbanX.Search.Application.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddOpenApi();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddApplication(builder.Configuration);

// Add Message queue
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(configureBus: bus =>
    {
        bus.AddConsumer<ProductCreatedConsumer>();
        bus.AddConsumer<ProductCatalogUpdatedConsumer>();
        bus.AddConsumer<ProductStatusChangedSearchConsumer>();
        bus.AddConsumer<ProductDeletedSearchConsumer>();
    });
var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();