using Microsoft.Extensions.DependencyInjection;
using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Persistence.Repositories;

namespace UrbanX.Inventory.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
        services.AddScoped<IInventoryReservationRepository, InventoryReservationRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        return services;
    }
}
