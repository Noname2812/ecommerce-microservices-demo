using Microsoft.Extensions.DependencyInjection;
using UrbanX.Identity.Domain;
using UrbanX.Identity.Persistence.Repositories;

namespace UrbanX.Identity.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IAuthAuditLogRepository, AuthAuditLogRepository>();
        return services;
    }
}
