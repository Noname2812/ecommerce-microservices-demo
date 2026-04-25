using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class StockMovementRepository(InventoryDbContext dbContext) : IStockMovementRepository;
