using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Payment.Application.Behaviors;
using UrbanX.Payment.Persistence.DependencyInjection.Extensions;

namespace UrbanX.Payment.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(
            assembly: AssemblyReference.Assembly,
            cfg =>
            {
                cfg.AddOpenBehavior(typeof(PaymentTransactionBehavior<,>));
            });
        services.AddPersistence();
        return services;
    }
}
