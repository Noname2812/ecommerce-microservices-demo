using Shared.Kernel.Domain;

namespace UrbanX.Promotion.Domain.Models;

public class Coupon : BaseEntity<string>
{
    // Code is the human-readable coupon identifier — maps to Id (PK)
    public string Code => Id;
    public string DiscountType { get; init; } = null!;
    public decimal DiscountValue { get; init; }
    public int? TotalQuota { get; init; }
    public int UsedQuota { get; set; }       // incremented on each successful claim
    public decimal MinOrderValue { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsActive { get; set; } = true; // can be deactivated
}
