using UrbanX.Inventory.Domain;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Persistence.Repositories;

public sealed class StockMovementRepository(InventoryDbContext dbContext) : IStockMovementRepository
{
    public Task AddAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        dbContext.StockMovements.Add(movement);
        return Task.CompletedTask;
    }
}
