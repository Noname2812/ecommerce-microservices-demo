using Microsoft.Extensions.DependencyInjection;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Persistence.Repositories;

namespace UrbanX.Payment.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProviderRepository, PaymentProviderRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IPaymentEventRepository, PaymentEventRepository>();
        return services;
    }
}
