using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SaleAllocationOptions>(configuration.GetSection(SaleAllocationOptions.SectionName));

        services.AddOptions<CatalogSnapshotOptions>()
            .BindConfiguration(CatalogSnapshotOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
            {
                client.BaseAddress = new Uri("http://promotion");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler();

        // L3 fallback only — short timeout because L1+L2 cover the hot path; HTTP is the safety net,
        // not the primary lookup. CatalogUnavailable propagates up when this fails.
        services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<CatalogSnapshotOptions>>().Value;
            client.BaseAddress = new Uri("http://catalog");
            client.Timeout = TimeSpan.FromMilliseconds(options.HttpFallbackTimeoutMilliseconds);
        });

        services.AddSingleton<ISaleAllocationGate, SaleAllocationGate>();

        services.AddScoped<IProductSnapshotCache, RedisProductSnapshotCache>();

        return services;
    }
}
