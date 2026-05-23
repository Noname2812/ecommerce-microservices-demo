using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Services;
using UrbanX.Payment.Infrastructure.DependencyInjection.Options;
using UrbanX.Payment.Infrastructure.Integrations.Momo;
using UrbanX.Payment.Infrastructure.Integrations.SePay;
using UrbanX.Payment.Infrastructure.Jobs;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<SePayOptions>, SePayOptionsValidator>();
        services
            .AddOptions<SePayOptions>()
            .BindConfiguration(SePayOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MomoOptions>, MomoOptionsValidator>();
        services
            .AddOptions<MomoOptions>()
            .BindConfiguration(MomoOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<PaymentBusinessOptions>()
            .BindConfiguration(PaymentBusinessOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<OrderCancelledConsumerOptions>, OrderCancelledConsumerOptionsValidator>();
        services
            .AddOptions<OrderCancelledConsumerOptions>()
            .BindConfiguration(OrderCancelledConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CreatePaymentSessionConsumerOptions>, CreatePaymentSessionConsumerOptionsValidator>();
        services
            .AddOptions<CreatePaymentSessionConsumerOptions>()
            .BindConfiguration(CreatePaymentSessionConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<PaymentExpirySweepJobOptions>()
            .BindConfiguration(PaymentExpirySweepJobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<PaymentExpirySweepJob>();

        // Payment session providers — resolved by Method string in CreatePaymentSessionCommandHandler
        services.AddScoped<IPaymentSessionProvider, SePayPaymentProvider>();
        services.AddScoped<IPaymentSessionProvider, MomoPaymentProvider>();

        // Refund providers — resolved by Method string in CompleteRefund handler
        services.AddScoped<IPaymentRefundProvider, MomoRefundProvider>();

        services.AddScoped<IAutoRefundService, AutoRefundService>();

        services.AddHttpClient<IMomoClient, MomoClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MomoOptions>>().Value;
            client.BaseAddress = new Uri(opts.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        }).AddStandardResilienceHandler();

        return services;
    }
}
