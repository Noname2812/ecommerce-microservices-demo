using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Persistence
{
    public sealed class CategoryRepository : ICategoryRepository
    {
        private readonly CatalogDbContext _db;

        public CategoryRepository(CatalogDbContext db) => _db = db;

        public Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
            _db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken);

        public async Task<Category?> GetByIdAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
            await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
    }
}
