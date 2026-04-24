using Shared.Kernel.Domain;

namespace UrbanX.Catalog.Domain.Models
{
    public class Brand : BaseEntity<Guid>
    {
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? LogoUrl { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
