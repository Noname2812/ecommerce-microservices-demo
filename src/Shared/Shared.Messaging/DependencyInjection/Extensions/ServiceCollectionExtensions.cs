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
        /// </summary>
        public static IServiceCollection AddMessaging(
            this IServiceCollection services,
            Action<IBusRegistrationConfigurator>? configureBus = null,
            params Assembly[] consumerAssemblies)
        {
            services.AddMassTransit(bus =>
            {
                bus.SetKebabCaseEndpointNameFormatter();

                foreach (var assembly in consumerAssemblies)
                    bus.AddConsumers(assembly);

                configureBus?.Invoke(bus);

                bus.UsingRabbitMq((ctx, cfg) =>
                {
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

                    // ── Throughput ─────
                    cfg.PrefetchCount = opt?.PrefetchCount ?? 16;
                    cfg.ConcurrentMessageLimit = opt?.ConcurrentMessageLimit ?? 8;

                    // ── Retry ───────────────────────────────────────
                    cfg.UseMessageRetry(r =>
                    {
                        r.Immediate(opt?.Retry.ImmediateCount ?? 1);
                        r.Interval(
                            opt?.Retry.DelayedCount ?? 3,
                            TimeSpan.FromSeconds(opt?.Retry.DelaySeconds ?? 5));

                        r.Ignore<ArgumentException>();
                        r.Ignore<InvalidOperationException>();
                    });

                    cfg.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), ctx);
                    cfg.ConfigureEndpoints(ctx);
                });
            });

            services.AddScoped<IEventPublisher, EventPublisher>();
            services.AddScoped<IMessageRequestClient, MessageRequestClient>();

            return services;
        }

        /// <summary>
        /// Registers MediatR with all standard pipeline behaviors.
        /// Call this once, passing all assemblies that contain handlers.
        /// </summary>
        public static IServiceCollection AddMediator(
            this IServiceCollection services,
            Assembly assembly,
            Action<MediatRServiceConfiguration>? configure = null)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IUserContext, UserHttpContext>();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);

                cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(IdempotencyPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(AuthorizationPipelineBehavior<,>));
                cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
                configure?.Invoke(cfg);
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