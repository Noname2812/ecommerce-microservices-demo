using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Order.Application.Behaviors;
using UrbanX.Order.Persistence.DependencyInjection.Extensions;

namespace UrbanX.Order.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(
            assembly: AssemblyReference.Assembly,
            cfg =>
            {
                cfg.AddOpenBehavior(typeof(OrderTransactionBehavior<,>));
            });
        services.AddPersistence();
        return services;
    }
}
