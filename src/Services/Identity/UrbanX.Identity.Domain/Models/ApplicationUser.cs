using Microsoft.AspNetCore.Identity;

namespace UrbanX.Identity.Domain.Models
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string DisplayName { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public Guid? MerchantId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DeactivatedAt { get; set; }
        public string? DeactivationReason { get; set; }

        public UserProfile? Profile { get; set; }
    }
}
