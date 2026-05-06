using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Promotion.Application.Jobs;
using UrbanX.Promotion.Application.Messaging;

namespace UrbanX.Promotion.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<CouponReleaseRequestedConsumerOptions>,
            CouponReleaseRequestedConsumerOptionsValidator>();

        services
            .AddOptions<CouponReleaseRequestedConsumerOptions>()
            .BindConfiguration(CouponReleaseRequestedConsumerOptions.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<ReleaseExpiredCouponClaimsJobOptions>()
            .BindConfiguration(ReleaseExpiredCouponClaimsJobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CouponReleaseRequestedProcessor>();
        services.AddScoped<ReleaseExpiredCouponClaimsJob>();
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
