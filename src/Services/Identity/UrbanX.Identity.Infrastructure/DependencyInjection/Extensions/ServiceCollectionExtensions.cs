using Microsoft.Extensions.DependencyInjection;
using UrbanX.Identity.Infrastructure.Audit;
using UrbanX.Identity.Infrastructure.Email;

namespace UrbanX.Identity.Infrastructure.DependencyInjection.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
        {
            services.AddScoped<IEmailSender, LogEmailSender>();
            services.AddScoped<IIdentityAuditWriter, IdentityAuditWriter>();
            return services;
        }
    }
}
