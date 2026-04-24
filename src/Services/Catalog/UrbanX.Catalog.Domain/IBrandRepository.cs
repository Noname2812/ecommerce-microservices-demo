using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Domain
{
    public interface IBrandRepository
    {
        Task<Brand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
