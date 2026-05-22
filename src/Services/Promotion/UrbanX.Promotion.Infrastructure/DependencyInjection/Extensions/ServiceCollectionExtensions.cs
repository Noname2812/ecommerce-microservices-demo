using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Options;
using UrbanX.Promotion.Infrastructure.Jobs;
using UrbanX.Promotion.Infrastructure.Redis;

namespace UrbanX.Promotion.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<CouponReleaseRequestedConsumerOptions>,
            CouponReleaseRequestedConsumerOptionsValidator>();

        services
            .AddOptions<CouponReleaseRequestedConsumerOptions>()
            .BindConfiguration(CouponReleaseRequestedConsumerOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ClaimCouponRequestedConsumerOptions>,
            ClaimCouponRequestedConsumerOptionsValidator>();

        services
            .AddOptions<ClaimCouponRequestedConsumerOptions>()
            .BindConfiguration(ClaimCouponRequestedConsumerOptions.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<ReleaseExpiredCouponClaimsJobOptions>()
            .BindConfiguration(ReleaseExpiredCouponClaimsJobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ReleaseExpiredCouponClaimsJob>();
        services.AddScoped<ICouponClaimRedisGateway, CouponClaimRedisGateway>();

        return services;
    }
}
