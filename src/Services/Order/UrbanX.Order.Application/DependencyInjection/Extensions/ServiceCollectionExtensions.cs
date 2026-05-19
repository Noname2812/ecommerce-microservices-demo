using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShippingOptions>(configuration.GetSection(ShippingOptions.SectionName));

        services.AddOptions<OrderPaymentOptions>()
            .BindConfiguration(OrderPaymentOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PlaceOrderOptions>()
            .BindConfiguration(PlaceOrderOptions.SectionName)
            .ValidateDataAnnotations()
            // Cross-field invariant: the coupon Redis lock must outlive the payment window so the
            // user cannot lose their coupon mid-checkout. We require at least a 60-second buffer
            // beyond the saga's payment expiry. Catches config drift at startup, not at 3 a.m.
            .Validate<IOptions<OrderPaymentOptions>>(
                (place, payment) =>
                    place.CouponLockTtlSeconds >= payment.Value.SalesOrderExpiryMinutes * 60 + 60,
                "PlaceOrder:CouponLockTtlSeconds must be >= Order:Payment:SalesOrderExpiryMinutes*60 + 60s buffer.")
            .ValidateOnStart();

        services.AddOptions<OrderTicketCacheOptions>()
            .BindConfiguration(OrderTicketCacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        services.AddScoped<IShippingValidator, ShippingValidator>();
        return services;
    }
}
