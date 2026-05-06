using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;

namespace UrbanX.Promotion.Persistence.Repositories;

public sealed class CouponRepository(PromotionDbContext db) : ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        db.Coupons.FirstOrDefaultAsync(c => c.Id == code, ct);

    public async Task AddAsync(Coupon coupon, CancellationToken ct = default) =>
        await db.Coupons.AddAsync(coupon, ct);

    public Task UpdateAsync(Coupon coupon, CancellationToken ct = default)
    {
        // EntityState.Modified is safe for both tracked and untracked entities.
        // db.Update() throws InvalidOperationException if the entity is already tracked.
        db.Entry(coupon).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task<int> TryDecrementUsedQuotaIfPositiveAsync(string couponCode, CancellationToken ct = default) =>
        db.Coupons
            .Where(c => c.Id == couponCode && c.UsedQuota > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedQuota, c => c.UsedQuota - 1), ct);
}
