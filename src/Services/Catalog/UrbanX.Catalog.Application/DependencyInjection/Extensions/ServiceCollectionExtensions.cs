using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Catalog.Application.Behaviors;
using UrbanX.Catalog.Persistence.DependencyInjection.Extensions;

namespace UrbanX.Catalog.Application.DependencyInjection.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMediator(
                assembly: AssemblyReference.Assembly,
                cfg =>
                {
                    cfg.AddOpenBehavior(typeof(CatalogTransactionBehavior<,>));
                });
            services.AddPersistence();
            return services;
        }
    }
}
