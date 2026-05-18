using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
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

        services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<CatalogClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseAddress);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler()
            .Configure((options, serviceProvider) =>
            {
                var resilience = serviceProvider
                    .GetRequiredService<IOptions<CatalogClientResilienceOptions>>()
                    .Value;
                ApplyCatalogClientResilience(options, resilience);
            });

        services.AddSingleton<IPendingOrderSlotService, RedisPendingOrderSlotService>();

        return services;
    }

    internal static void ApplyCatalogClientResilience(
        HttpStandardResilienceOptions options,
        CatalogClientResilienceOptions resilience)
    {
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(resilience.CbSamplingDurationSeconds);
        options.CircuitBreaker.FailureRatio = resilience.CbFailureRatio;
        options.CircuitBreaker.MinimumThroughput = resilience.CbMinimumThroughput;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(resilience.CbBreakDurationSeconds);

        options.Retry.MaxRetryAttempts = resilience.RetryMaxAttempts;
        options.Retry.UseJitter = true;
        options.Retry.BackoffType = DelayBackoffType.Exponential;

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(resilience.AttemptTimeoutSeconds);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(resilience.TotalTimeoutSeconds);
    }
}
