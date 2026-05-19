using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Abstractions;
using Shared.Messaging.Authorization;
using Shared.Messaging.Behaviors;
using Shared.Messaging.DependencyInjection;
using Shared.Messaging.DependencyInjection.Options;
using Shared.Messaging.Filters;
using Shared.Messaging.Fomatters;
using System.Reflection;

namespace Shared.Messaging.DependencyInjection.Extensions
{
    /// <summary>
    /// Entry point for all SharedKernel DI registrations.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>Registers core SharedKernel services and returns a fluent builder.</summary>
        public static IServiceCollection AddConfigMessaging(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<RabbitMqOptions>(
                configuration.GetSection(RabbitMqOptions.SectionName));

            return services;
        }

        /// <summary>
        /// Registers MassTransit with RabbitMQ transport and MediatR pipeline behaviors.
        /// Pass a delegate to register consumers, sagas, and state machines.
        /// Also registers <c>/health</c> RabbitMQ connectivity when connection can be resolved from configuration.
        /// Does not register bus-wide <c>UseMessageRetry</c>, <c>PrefetchCount</c>, or <c>ConcurrentMessageLimit</c>;
        /// configure retry and throughput per receive endpoint or <c>ConsumerDefinition</c> when needed.
        /// </summary>
        public static IServiceCollection AddMessaging(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<IBusRegistrationConfigurator>? configureBus = null,
            params Assembly[] consumerAssemblies)
        {
            services.AddMassTransit(bus =>
            {
                bus.SetKebabCaseEndpointNameFormatter();
                // Publish-based scheduler for saga Schedule(...) / timeout messages.
                // Avoids queue:scheduler&bind=false (invalid RabbitMQ entity name) and does not need the delayed-exchange plugin.
                bus.AddPublishMessageScheduler();

                foreach (var assembly in consumerAssemblies)
                    bus.AddConsumers(assembly);

                configureBus?.Invoke(bus);

                bus.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.UsePublishMessageScheduler();
                    // Exclude base types
                    cfg.PublishTopology.GetMessageTopology<IIntegrationEvent>().Exclude = true;
                    cfg.PublishTopology.GetMessageTopology<IntegrationEventBase>().Exclude = true;

                    // Customize exchange names
                    cfg.MessageTopology.SetEntityNameFormatter(new KebabCaseEntityNameFormatter());

                    var configuration = ctx.GetRequiredService<IConfiguration>();
                    var opt = ctx.GetService<IOptions<RabbitMqOptions>>()?.Value;

                    // ── Connection ──────────────────────────────────
                    var aspireConnectionString = configuration.GetConnectionString("messaging");

                    if (!string.IsNullOrEmpty(aspireConnectionString))
                    {
                        // Aspire inject: amqp://guest:guest@localhost:5672
                        cfg.Host(new Uri(aspireConnectionString), h =>
                        {
                            if (opt?.PublisherConfirms == true)
                                h.PublisherConfirmation = true;
                        });
                    }
                    else
                    {
                        if (opt is null)
                            throw new InvalidOperationException(
                                "RabbitMQ chưa được cấu hình. " +
                                "Using Aspire (AppHost.WithReference) or Add 'Shared:RabbitMQ' in appsettings.");

                        cfg.Host(
                            new Uri($"rabbitmq://{opt.Host}:{opt.Port}/{opt.VirtualHost}"),
                            h =>
                            {
                                h.Username(opt.Username);
                                h.Password(opt.Password);
                                if (opt.PublisherConfirms)
                                    h.PublisherConfirmation = true;
                            });
                    }

                    cfg.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), ctx);
                    cfg.ConfigureEndpoints(ctx);
                });
            });

            services.AddScoped<IEventPublisher, EventPublisher>();
            services.AddScoped<IMessageRequestClient, MessageRequestClient>();

            var amqpUri = RabbitMqConnectionResolver.ResolveAmqpUri(configuration);
            if (!string.IsNullOrWhiteSpace(amqpUri))
            {
                services.AddHealthChecks()
                    .AddRabbitMQ(
                        rabbitConnectionString: amqpUri.Trim(),
                        name: "rabbitmq",
                        tags: ["ready", "messaging"]);
            }

            return services;
        }

        /// <summary>
        /// Registers MediatR with all standard pipeline behaviors.
        /// Call this once, passing all assemblies that contain handlers.
        /// </summary>
        public static IServiceCollection AddMediatorWithPielineDefault(
            this IServiceCollection services,
            Assembly assembly)
        {
            services.AddMemoryCache();
            // RedisCircuitBreaker is registered by Shared.Cache (AddSharedCache).

            services.AddHttpContextAccessor();
            services.AddScoped<IUserContext, UserHttpContext>();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);

                cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(AuthorizationPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(CacheQueryPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(IdempotencyPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(DistributedLockPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(TransactionPipelineBehavior<,>));
            });


            services.AddValidatorsFromAssembly(assembly);

            return services;
        }

        /// <summary>
        /// Registers a MassTransit saga state machine with EF Core persistence.
        /// </summary>
        public static IServiceCollection AddSagaPersistence<TDbContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder>? configureDb = null)
            where TDbContext : DbContext
        {
            if (configureDb is not null)
                services.AddDbContext<TDbContext>(configureDb);

            return services;
        }
    }
}