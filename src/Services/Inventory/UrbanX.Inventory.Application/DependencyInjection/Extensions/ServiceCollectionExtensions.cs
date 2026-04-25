using MediatR;
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
        services.AddMediator(AssemblyReference.Assembly);
        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(InventoryTransactionBehavior<,>));
        services.AddPersistence();
        return services;
    }
}
