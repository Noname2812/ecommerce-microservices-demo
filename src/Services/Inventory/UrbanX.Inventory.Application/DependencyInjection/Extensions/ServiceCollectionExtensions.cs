using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.Behaviors;
using UrbanX.Inventory.Persistence.DependencyInjection.Extensions;

namespace UrbanX.Inventory.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(
            assembly: AssemblyReference.Assembly,
            configure: options =>
            {
                options.AddOpenBehavior(typeof(InventoryTransactionBehavior<,>));
            }
        );
        services.AddPersistence();
        return services;
    }
}
