using Microsoft.Extensions.DependencyInjection;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Infrastructure.Redis;

namespace UrbanX.Promotion.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromotionInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICouponClaimRedisGateway, CouponClaimRedisGateway>();
        return services;
    }
}
