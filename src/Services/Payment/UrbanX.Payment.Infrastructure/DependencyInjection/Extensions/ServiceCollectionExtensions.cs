using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Infrastructure.DependencyInjection.Options;
using UrbanX.Payment.Infrastructure.Jobs;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services
            .AddOptions<SePayOptions>()
            .BindConfiguration(SePayOptions.SectionName)
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

        return services;
    }
}
