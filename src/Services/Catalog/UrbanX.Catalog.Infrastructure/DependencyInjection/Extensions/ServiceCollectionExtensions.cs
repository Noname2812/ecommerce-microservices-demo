using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Catalog.Application.Abstractions;
using UrbanX.Catalog.Infrastructure.DependencyInjection.Options;
using UrbanX.Catalog.Infrastructure.Services;

namespace UrbanX.Catalog.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<CatalogProjectionConsumerOptions>,
            CatalogProjectionConsumerOptionsValidator>();

        services
            .AddOptions<CatalogProjectionConsumerOptions>()
            .BindConfiguration(CatalogProjectionConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IInventoryServiceClient, InventoryServiceClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["Services:Inventory"]
                ?? throw new InvalidOperationException("Services:Inventory is not configured."));
        });

        return services;
    }
}
