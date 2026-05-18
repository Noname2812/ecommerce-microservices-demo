using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogClientOptions>()
            .BindConfiguration(CatalogClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<CatalogClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseAddress);
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
