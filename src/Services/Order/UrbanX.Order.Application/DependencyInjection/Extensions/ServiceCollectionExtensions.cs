using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Options;
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

        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        services.AddScoped<IShippingValidator, ShippingValidator>();
        return services;
    }
}
