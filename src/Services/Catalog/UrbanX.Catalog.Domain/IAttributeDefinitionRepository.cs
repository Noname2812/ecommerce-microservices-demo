using UrbanX.Catalog.Domain.Models;

namespace UrbanX.Catalog.Domain
{
    public interface IAttributeDefinitionRepository
    {
        /// <summary>Gets or creates an attribute row for a category; used when mapping API name/value to FK.</summary>
        Task<AttributeDefinition> GetOrCreateAsync(
            Guid? categoryId,
            string name,
            string type,
            bool isVariant,
            int displayOrder,
            CancellationToken cancellationToken = default);
    }
}
