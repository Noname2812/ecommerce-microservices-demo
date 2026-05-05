using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Persistence.Repositories;

namespace UrbanX.Promotion.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IPromotionUsageRepository, PromotionUsageRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<ICouponClaimRepository, CouponClaimRepository>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        return services;
    }
}
