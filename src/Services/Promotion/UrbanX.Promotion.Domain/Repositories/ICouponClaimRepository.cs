using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface ICouponClaimRepository
{
    /// <summary>
    /// Loads the claim detached (implementation uses AsNoTracking). Safe with <see cref="TryMarkReleasedIfClaimedAsync"/> which updates via ExecuteUpdate bypassing EF change-tracker —
    /// avoids re-saving a stale <c>CLAIMED</c> row if callers later mutate the instance incorrectly.
    /// </summary>
    Task<CouponClaim?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CouponClaim?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(CouponClaim claim, CancellationToken ct = default);
    Task UpdateAsync(CouponClaim claim, CancellationToken ct = default);

    /// <returns>Rows updated (1 if transitioned CLAIMED → RELEASED).</returns>
    Task<int> TryMarkReleasedIfClaimedAsync(Guid id, DateTimeOffset releasedAt, CancellationToken ct = default);
}
