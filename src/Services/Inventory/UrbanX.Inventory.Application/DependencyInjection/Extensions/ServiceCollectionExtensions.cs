using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.Jobs;
using UrbanX.Inventory.Application.Messaging;

namespace UrbanX.Inventory.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<InventoryReleaseRequestedConsumerOptions>,
            InventoryReleaseRequestedConsumerOptionsValidator>();

        services
            .AddOptions<InventoryReleaseRequestedConsumerOptions>()
            .BindConfiguration(InventoryReleaseRequestedConsumerOptions.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<ReleaseExpiredReservationsJobOptions>()
            .BindConfiguration(ReleaseExpiredReservationsJobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<InventoryReleaseRequestedProcessor>();
        services.AddScoped<ReleaseExpiredReservationsJob>();
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
