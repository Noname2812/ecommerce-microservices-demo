using Microsoft.Extensions.DependencyInjection;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // "promotion" matches the Aspire service name; service discovery resolves the actual URL
        services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
        {
            client.BaseAddress = new Uri("http://promotion");
        });

        return services;
    }
}
