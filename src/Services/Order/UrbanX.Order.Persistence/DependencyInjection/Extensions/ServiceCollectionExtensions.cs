using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions;
using UrbanX.Order.Application.ReadModels;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Persistence.Repositories;
using UrbanX.Order.Persistence.Repositories.Read;

namespace UrbanX.Order.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISalesOrderStatusQuery, SalesOrderStatusQuery>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        services.AddScoped<ICatalogSnapshotReader, DapperCatalogSnapshotReader>();
        services.AddScoped<ICatalogSnapshotWriter, DapperCatalogSnapshotWriter>();
        return services;
    }
}
