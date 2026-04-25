using Microsoft.Extensions.DependencyInjection;

namespace UrbanX.Inventory.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // Repository registrations sẽ được thêm theo từng entity
        return services;
    }
}
