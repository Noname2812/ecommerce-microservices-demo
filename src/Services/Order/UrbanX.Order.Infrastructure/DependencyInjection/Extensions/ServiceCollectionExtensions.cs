using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Refit;
using UrbanX.Order.Infrastructure.Services;
using Shared.Kernel;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InventoryClientOptions>(configuration.GetSection(InventoryClientOptions.SectionName));
        services.Configure<CouponClientOptions>(configuration.GetSection(CouponClientOptions.SectionName));
        services.Configure<SaleAllocationOptions>(configuration.GetSection(SaleAllocationOptions.SectionName));

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonOptions)
        };

        services.AddRefitClient<IInventoryApi>(refitSettings)
            .ConfigureHttpClient((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<InventoryClientOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl) ? "http://inventory" : opt.BaseUrl.TrimEnd('/');
                client.BaseAddress = new Uri(baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddResilienceHandler("inventory-5xx-retry", static builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(250),
                    BackoffType = DelayBackoffType.Exponential,
                    MaxDelay = TimeSpan.FromSeconds(4),
                    ShouldHandle = static args =>
                    {
                        if (args.Outcome.Result is HttpResponseMessage response)
                        {
                            var code = (int)response.StatusCode;
                            return ValueTask.FromResult(code >= 500 && code < 600);
                        }

                        return ValueTask.FromResult(false);
                    }
                });
            });

        services.AddScoped<IInventoryClient, InventoryClient>();

        services.AddRefitClient<ICouponApi>(refitSettings)
            .ConfigureHttpClient((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<CouponClientOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl) ? "http://promotion" : opt.BaseUrl.TrimEnd('/');
                client.BaseAddress = new Uri(baseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddResilienceHandler("coupon-5xx-retry", static builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(250),
                    BackoffType = DelayBackoffType.Exponential,
                    MaxDelay = TimeSpan.FromSeconds(4),
                    ShouldHandle = static args =>
                    {
                        if (args.Outcome.Exception is HttpRequestException)
                            return ValueTask.FromResult(true);

                        if (args.Outcome.Result is HttpResponseMessage response)
                        {
                            var code = (int)response.StatusCode;
                            return ValueTask.FromResult(code >= 500 && code < 600);
                        }

                        return ValueTask.FromResult(false);
                    }
                });
            });

        services.AddScoped<ICouponClient, CouponClient>();

        // Promotion HTTP: standard resilience (timeouts, retries, circuit breaker) for all client methods.
        services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
            {
                client.BaseAddress = new Uri("http://promotion");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(client =>
        {
            client.BaseAddress = new Uri("http://catalog");
        });

        services.AddScoped<ICompensationCollector, CompensationCollector>();
        services.AddSingleton<ISaleAllocationGate, SaleAllocationGate>();

        return services;
    }
}
