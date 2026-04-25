using UrbanX.Inventory.Domain;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class InventoryReservationRepository(InventoryDbContext dbContext) : IInventoryReservationRepository;
