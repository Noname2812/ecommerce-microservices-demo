using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Persistence.Repositories;

namespace UrbanX.Order.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        return services;
    }
}
