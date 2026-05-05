using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class CouponClaimRepository(PromotionDbContext db) : ICouponClaimRepository
{
    public Task<CouponClaim?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CouponClaims.FirstOrDefaultAsync(c => c.Id == id, ct);

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
}
