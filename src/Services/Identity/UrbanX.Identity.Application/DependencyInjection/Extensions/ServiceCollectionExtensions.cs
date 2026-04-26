using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Identity.Application.Behaviors;
using UrbanX.Identity.Infrastructure.DependencyInjection.Extensions;
using UrbanX.Identity.Persistence.DependencyInjection.Extensions;

namespace UrbanX.Identity.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(
            assembly: AssemblyReference.Assembly,
            cfg =>
            {
                cfg.AddOpenBehavior(typeof(IdentityTransactionBehavior<,>));
            });
        services.AddPersistence();
        services.AddIdentityInfrastructure();
        return services;
    }
}
