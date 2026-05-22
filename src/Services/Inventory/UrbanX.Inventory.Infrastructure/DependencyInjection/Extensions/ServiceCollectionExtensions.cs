using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Inventory.Infrastructure.DependencyInjection.Options;
using UrbanX.Inventory.Infrastructure.Jobs;

namespace UrbanX.Inventory.Infrastructure.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<InventoryReleaseRequestedConsumerOptions>,
            InventoryReleaseRequestedConsumerOptionsValidator>();

        services
            .AddOptions<InventoryReleaseRequestedConsumerOptions>()
            .BindConfiguration(InventoryReleaseRequestedConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ReserveInventoryRequestedConsumerOptions>,
            ReserveInventoryRequestedConsumerOptionsValidator>();

        services
            .AddOptions<ReserveInventoryRequestedConsumerOptions>()
            .BindConfiguration(ReserveInventoryRequestedConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ConfirmInventoryRequestedConsumerOptions>,
            ConfirmInventoryRequestedConsumerOptionsValidator>();

        services
            .AddOptions<ConfirmInventoryRequestedConsumerOptions>()
            .BindConfiguration(ConfirmInventoryRequestedConsumerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<ReleaseExpiredReservationsJobOptions>()
            .BindConfiguration(ReleaseExpiredReservationsJobOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ReleaseExpiredReservationsJob>();

        return services;
    }
}
