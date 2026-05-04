using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Outbox.Abstractions;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;

namespace Shared.Outbox.DependencyInjection.Extensions
{
    public static class CompensationOutboxServiceCollectionExtensions
    {
        /// <summary>
        /// Registers compensation outbox writer, repository, and <see cref="CompensationOutboxRelayWorker"/>.
        /// Requires <see cref="OutboxDbContext"/> (call <c>AddOutbox&lt;TDbContext&gt;</c> first on the same service collection).
        /// </summary>
        public static IServiceCollection AddCompensationOutbox(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            if (configuration is not null)
            {
                services.Configure<CompensationOutboxOptions>(
                    configuration.GetSection(CompensationOutboxOptions.SectionName));
            }
            else
            {
                services.Configure<CompensationOutboxOptions>(_ => { });
            }

            services.TryAddScoped<ICompensationOutboxRepository, CompensationOutboxRepository>();
            services.TryAddScoped<ICompensationOutboxWriter, CompensationOutboxWriter>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, CompensationOutboxRelayWorker>());

            return services;
        }
    }
}
