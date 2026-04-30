using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrbanX.Catalog.Application.Abstractions;
using UrbanX.Catalog.Infrastructure;

namespace UrbanX.Catalog.Application.DependencyInjection.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient<IInventoryServiceClient, InventoryServiceClient>(client =>
            {
                client.BaseAddress = new Uri(config["Services:Inventory"] ?? throw new InvalidOperationException("Inventory service URL is not configured."));
            });

            return services;
        }
    }
}
