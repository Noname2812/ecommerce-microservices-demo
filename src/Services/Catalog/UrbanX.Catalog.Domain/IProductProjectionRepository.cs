using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Domain;

public interface IProductProjectionRepository
{
    Task UpsertAsync(Product product, CancellationToken ct = default);
}
