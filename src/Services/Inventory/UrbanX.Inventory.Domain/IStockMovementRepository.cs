using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Domain;

public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken cancellationToken);
}
