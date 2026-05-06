using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class CouponClaimRepository(PromotionDbContext db) : ICouponClaimRepository
{
    public Task<CouponClaim?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CouponClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CouponClaim?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.CouponClaims.FirstOrDefaultAsync(c => c.OrderIdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(CouponClaim claim, CancellationToken ct = default) =>
        await db.CouponClaims.AddAsync(claim, ct);

    public Task UpdateAsync(CouponClaim claim, CancellationToken ct = default)
    {
        // EntityState.Modified is safe for both tracked and untracked entities.
        // db.Update() throws InvalidOperationException if the entity is already tracked.
        db.Entry(claim).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task<int> TryMarkReleasedIfClaimedAsync(Guid id, DateTimeOffset releasedAt, CancellationToken ct = default) =>
        db.CouponClaims
            .Where(c => c.Id == id && c.Status == CouponClaimStatus.Claimed)
            .ExecuteUpdateAsync(
                x => x
                    .SetProperty(c => c.Status, CouponClaimStatus.Released)
                    .SetProperty(c => c.ReleasedAt, releasedAt),
                ct);
}
