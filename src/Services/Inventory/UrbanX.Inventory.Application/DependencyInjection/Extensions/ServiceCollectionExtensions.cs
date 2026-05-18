using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Messaging.DependencyInjection.Extensions;
using UrbanX.Inventory.Application.Jobs;
using UrbanX.Inventory.Application.Messaging;
using UrbanX.Inventory.Application.DependencyInjection.Options;

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

        services.AddScoped<InventoryReleaseRequestedProcessor>();
        services.AddScoped<ReserveInventoryRequestedProcessor>();
        services.AddScoped<ConfirmInventoryRequestedProcessor>();
        services.AddScoped<ReleaseExpiredReservationsJob>();
        services.AddMediatorWithPielineDefault(AssemblyReference.Assembly);
        return services;
    }
}
