using Shared.Kernel.Domain;

namespace UrbanX.Identity.Domain.Models
{
    public class AuthAuditLog : BaseEntity<Guid>
    {
        public Guid? UserId { get; set; }
        public string? Email { get; set; }
        public string EventType { get; set; } = null!;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? MetadataJson { get; set; }
        public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
