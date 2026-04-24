using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models
{
    /// <summary>Nested product category. Materialized path and depth support subtree queries.</summary>
    public class Category : BaseEntity<Guid>
    {
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Example: /electronics/phones/android</summary>
        public string? Path { get; set; }
        public int Depth { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public Category? Parent { get; set; }
        public ICollection<Category> Children { get; set; } = new List<Category>();
    }
}
