using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.Abstractions.Catalog;
using UrbanX.Order.Application.Abstractions.Promotion;
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

        services.AddOptions<SaleSnapshotOptions>()
            .BindConfiguration(SaleSnapshotOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddSingleton<ISaleAllocationGate, SaleAllocationGate>();

        services.AddScoped<IProductSnapshotCache, RedisProductSnapshotCache>();
        services.AddScoped<ISaleSnapshotCache, MemoryRedisSaleSnapshotCache>();

        return services;
    }
}
