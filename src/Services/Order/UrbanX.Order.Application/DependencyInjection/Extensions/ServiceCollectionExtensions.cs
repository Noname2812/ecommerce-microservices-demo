using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Order.Application.Options;
using UrbanX.Order.Application.Usecases.V1.Command;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

namespace UrbanX.Order.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShippingOptions>(configuration.GetSection(ShippingOptions.SectionName));

        services.AddScoped<PlaceOrderCompensationContext>();
        services.AddScoped<PlaceSalesOrderCompensationContext>();
        services.AddScoped<IPipelineBehavior<PlaceOrderCommand, Result<Guid>>, PlaceOrderCompensationBehavior>();
        services.AddScoped<IPipelineBehavior<PlaceSalesOrderCommand, Result<Guid>>, PlaceSalesOrderCompensationBehavior>();

        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        services.AddScoped<IProductValidator, ProductValidator>();
        services.AddScoped<IShippingValidator, ShippingValidator>();
        services.AddScoped<IPricingValidator, PricingValidator>();
        services.AddScoped<ISaleEligibilityValidator, SaleEligibilityValidator>();
        services.AddScoped<ISalePricingValidator, SalePricingValidator>();
        return services;
    }
}
