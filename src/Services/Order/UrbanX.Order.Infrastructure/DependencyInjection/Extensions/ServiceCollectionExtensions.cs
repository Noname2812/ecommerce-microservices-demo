using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SaleAllocationOptions>(configuration.GetSection(SaleAllocationOptions.SectionName));

        services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
            {
                client.BaseAddress = new Uri("http://promotion");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(client =>
        {
            client.BaseAddress = new Uri("http://catalog");
        });

        services.AddSingleton<ISaleAllocationGate, SaleAllocationGate>();

        return services;
    }
}
