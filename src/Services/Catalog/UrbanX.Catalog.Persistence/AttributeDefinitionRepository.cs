using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Persistence
{
    public sealed class AttributeDefinitionRepository : IAttributeDefinitionRepository
    {
        private readonly CatalogDbContext _db;

        public AttributeDefinitionRepository(CatalogDbContext db) => _db = db;

        public async Task<AttributeDefinition> GetOrCreateAsync(
            Guid? categoryId,
            string name,
            string type,
            bool isVariant,
            int displayOrder,
            CancellationToken cancellationToken = default)
        {
            var trimmed = name.Trim();

            var tracked = _db.AttributeDefinitions.Local
                .FirstOrDefault(a => a.CategoryId == categoryId && a.Name == trimmed);
            if (tracked is not null)
                return tracked;

            var found = await _db.AttributeDefinitions
                .FirstOrDefaultAsync(
                    a => a.CategoryId == categoryId && a.Name == trimmed,
                    cancellationToken);

            if (found is not null)
                return found;

            var def = new AttributeDefinition
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                Name = trimmed,
                Type = type,
                IsVariantAttribute = isVariant,
                DisplayOrder = displayOrder
            };
            _db.AttributeDefinitions.Add(def);
            return def;
        }
    }
}
