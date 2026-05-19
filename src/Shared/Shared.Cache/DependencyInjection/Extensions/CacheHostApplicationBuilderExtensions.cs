using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Cache.Implementations;
using Shared.Cache.Resilience;
using StackExchange.Redis;

namespace Shared.Cache.DependencyInjection.Extensions;

public static class CacheHostApplicationBuilderExtensions
{
    /// <summary>
    /// Register shared Redis cache services.
    /// <para>
    /// Registers <see cref="IConnectionMultiplexer"/> via Aspire service discovery,
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> backed by Redis
    /// (satisfies <c>IdempotencyPipelineBehavior</c>), and <see cref="ICacheService"/> /
    /// <see cref="IDistributedLockService"/> for application use.
    /// </para>
    /// </summary>
    /// <param name="connectionName">Aspire resource name (default: "redis").</param>
    public static IHostApplicationBuilder AddSharedCache(
        this IHostApplicationBuilder builder,
        string connectionName = "redis")
    {
        // IConnectionMultiplexer — resolved by Aspire service discovery
        // Fail fast on Redis congestion — circuit trips in 2s instead of 5s default.
        builder.AddRedisClient(connectionName, configureOptions: options =>
        {
            options.SyncTimeout = 2000;
            options.AsyncTimeout = 2000;
        });

        // Bind CacheOptions from appsettings "Shared:Cache"
        builder.Services.Configure<CacheOptions>(
            builder.Configuration.GetSection(CacheOptions.SectionName));

        // IDistributedCache backed by Redis (used by IdempotencyPipelineBehavior).
        // Use OptionsBuilder.Configure<TDep> so IConnectionMultiplexer is injected
        // after the DI container is built — avoids DI timing issues.
        builder.Services.AddStackExchangeRedisCache(_ => { });
        builder.Services.AddOptions<RedisCacheOptions>()
            .Configure<IConnectionMultiplexer, IConfiguration>((opt, mux, cfg) =>
            {
                opt.ConnectionMultiplexerFactory = () => Task.FromResult(mux);
                var name = cfg[$"{CacheOptions.SectionName}:InstanceName"];
                opt.InstanceName = string.IsNullOrWhiteSpace(name) ? "urbanx:" : $"{name}:";
            });

        // Circuit breaker is shared across cache, lock, and pipeline behaviors —
        // a single failure in any path opens the circuit for all of them.
        builder.Services.AddSingleton<RedisCircuitBreaker>();

        builder.Services.AddSingleton<ICacheService, RedisCacheService>();
        builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();

        return builder;
    }
}
