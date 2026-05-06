using Shared.Kernel.Domain;

namespace UrbanX.Promotion.Domain.Models;

public class CouponClaim : BaseEntity<Guid>
{
    public string CouponCode { get; init; } = null!;
    public Guid UserId { get; init; }
    public string OrderIdempotencyKey { get; init; } = null!;
    public decimal DiscountAmount { get; init; }
    public string Status { get; set; } = null!;          // transitions: CLAIMED → RELEASED | EXPIRED
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; set; }      // set when claim is released

    /// <summary>Snapshot at claim time: release must INCR Redis quota if the coupon had a total quota when claimed.</summary>
    public bool RestoreQuotaSlotOnRelease { get; init; }
}
