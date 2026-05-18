using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<CatalogClientOptions>()
            .BindConfiguration(CatalogClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CatalogClientResilienceOptions>()
            .BindConfiguration(CatalogClientResilienceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PromotionClientOptions>()
            .BindConfiguration(PromotionClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PromotionClientResilienceOptions>()
            .BindConfiguration(PromotionClientResilienceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddResilientHttpClient<ICatalogServiceClient, CatalogServiceClient, CatalogClientOptions, CatalogClientResilienceOptions>(
            o => o.BaseAddress,
            aspireServiceName: "catalog");

        services.AddResilientHttpClient<ISaleEligibilityService, PromotionSaleEligibilityClient, PromotionClientOptions, PromotionClientResilienceOptions>(
            o => o.BaseAddress,
            aspireServiceName: "promotion");

        services.AddSingleton<IPendingOrderSlotService, RedisPendingOrderSlotService>();
        services.AddSingleton<IFlashSaleStockService, RedisFlashSaleStockService>();
        services.AddSingleton<ICouponLockService, RedisCouponLockService>();

        return services;
    }

    /// <summary>
    /// Registers a typed <see cref="HttpClient"/> with the standard Polly resilience handler whose
    /// circuit-breaker, retry, and timeout knobs are bound to <typeparamref name="TResilience"/>.
    /// Lets every outbound client (Catalog, Promotion, …) share one configuration code path.
    /// </summary>
    private static void AddResilientHttpClient<TClient, TImpl, TClientOptions, TResilience>(
        this IServiceCollection services,
        Func<TClientOptions, string> baseAddressSelector,
        string aspireServiceName)
        where TClient : class
        where TImpl : class, TClient
        where TClientOptions : class
        where TResilience : class, IHttpClientResilienceOptions
    {
        services.AddHttpClient<TClient, TImpl>()
            .ConfigureHttpClient((sp, client) =>
            {
                var config  = sp.GetRequiredService<IConfiguration>();
                var options = sp.GetRequiredService<IOptions<TClientOptions>>().Value;
                var baseUrl = ServiceEndpointResolver.Resolve(
                    config,
                    aspireServiceName,
                    baseAddressSelector(options));
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout     = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler()
            .Configure((options, serviceProvider) =>
            {
                var resilience    = serviceProvider.GetRequiredService<IOptions<TResilience>>().Value;
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var logger        = loggerFactory.CreateLogger($"HttpResilience.{typeof(TImpl).Name}");
                ApplyResilience(options, resilience, logger);
            });
    }

    internal static void ApplyResilience(
        HttpStandardResilienceOptions options,
        IHttpClientResilienceOptions r,
        ILogger logger)
    {
        options.CircuitBreaker.SamplingDuration  = TimeSpan.FromSeconds(r.CbSamplingDurationSeconds);
        options.CircuitBreaker.FailureRatio      = r.CbFailureRatio;
        options.CircuitBreaker.MinimumThroughput = r.CbMinimumThroughput;
        options.CircuitBreaker.BreakDuration     = TimeSpan.FromSeconds(r.CbBreakDurationSeconds);

        options.Retry.MaxRetryAttempts = r.RetryMaxAttempts;
        options.Retry.UseJitter        = true;
        options.Retry.BackoffType      = DelayBackoffType.Exponential;
        // Observability: log every retry so a transient-but-eventually-successful upstream blip
        // is still visible. Without this, retries are silent and on-call has no signal that the
        // downstream is degraded until the circuit opens.
        options.Retry.OnRetry = OnRetryHandler(logger);

        options.AttemptTimeout.Timeout      = TimeSpan.FromSeconds(r.AttemptTimeoutSeconds);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(r.TotalTimeoutSeconds);
    }

    private static Func<OnRetryArguments<HttpResponseMessage>, ValueTask> OnRetryHandler(ILogger logger) =>
        args =>
        {
            logger.LogWarning(
                args.Outcome.Exception,
                "HTTP retry attempt {Attempt} after {Delay} (status={Status})",
                args.AttemptNumber + 1,
                args.RetryDelay,
                args.Outcome.Result?.StatusCode);
            return ValueTask.CompletedTask;
        };
}
