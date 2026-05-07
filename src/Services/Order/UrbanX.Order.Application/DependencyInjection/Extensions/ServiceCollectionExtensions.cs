using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Kernel.Primitives;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Order.Application.Usecases.V1.Command;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PlaceOrderCompensationContext>();
        services.AddScoped<IPipelineBehavior<PlaceOrderCommand, Result<Guid>>, PlaceOrderCompensationBehavior>();

        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        services.AddScoped<IProductValidator, ProductValidator>();
        services.AddScoped<IShippingValidator, ShippingValidator>();
        services.AddScoped<IPricingValidator, PricingValidator>();
        return services;
    }
}
