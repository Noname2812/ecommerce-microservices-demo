using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;

namespace UrbanX.Payment.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
