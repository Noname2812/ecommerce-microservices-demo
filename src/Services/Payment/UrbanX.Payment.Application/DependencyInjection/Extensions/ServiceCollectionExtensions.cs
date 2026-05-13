using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Payment.Application.Configuration;

namespace UrbanX.Payment.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SePayOptions>(configuration.GetSection(SePayOptions.SectionName));
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
