using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class InventoryItemRepository(InventoryDbContext dbContext) : IInventoryItemRepository;
