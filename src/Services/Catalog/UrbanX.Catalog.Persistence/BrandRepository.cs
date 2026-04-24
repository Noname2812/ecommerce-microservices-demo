using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Persistence
{
    public sealed class BrandRepository : IBrandRepository
    {
        private readonly CatalogDbContext _db;

        public BrandRepository(CatalogDbContext db) => _db = db;

        public Task<Brand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }
}
