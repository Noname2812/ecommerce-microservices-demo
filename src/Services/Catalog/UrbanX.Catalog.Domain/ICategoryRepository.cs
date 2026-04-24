using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Domain
{
    public interface ICategoryRepository
    {
        Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
        Task<Category?> GetByIdAsync(Guid categoryId, CancellationToken cancellationToken = default);
    }
}
