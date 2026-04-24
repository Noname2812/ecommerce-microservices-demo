using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Outbox.Abstractions;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;

namespace Shared.Outbox.DependencyInjection.Extensions
{
    public static class OutboxServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Transactional Outbox pattern.
        ///
        /// TDbContext can be:
        ///  (a) Your application's own DbContext if it inherits OutboxDbContext
        ///  (b) OutboxDbContext itself when using a dedicated outbox connection
        ///
        /// Example:
        /// <code>
        /// builder.Services.AddOutbox<AppDbContext>(options =>
        ///     options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
        /// </code>
        /// </summary>
        public static IServiceCollection AddOutbox<TDbContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder>? configureDb = null,
            IConfiguration? configuration = null)
            where TDbContext : OutboxDbContext
        {
            // Register DbContext 
            if (configureDb is not null)
            {
                services.AddDbContext<TDbContext>(configureDb);
            }

            if (typeof(TDbContext) != typeof(OutboxDbContext))
            {
                services.TryAddScoped<OutboxDbContext>(sp => sp.GetRequiredService<TDbContext>());
            }

            // Bind options
            if (configuration is not null)
            {
                services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
            }
            else
            {
                services.Configure<OutboxOptions>(_ => { });
            }

            // Register outbox infrastructure
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<IOutboxWriter, OutboxWriter>();

            // Register the background relay worker
            services.AddHostedService<OutboxRelayWorker>();

            return services;
        }
    }
}
