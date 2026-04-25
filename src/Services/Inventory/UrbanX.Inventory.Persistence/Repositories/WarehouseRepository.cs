using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class WarehouseRepository(InventoryDbContext dbContext) : IWarehouseRepository;
