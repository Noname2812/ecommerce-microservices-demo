using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface ICouponRepository
{
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task AddAsync(Coupon coupon, CancellationToken ct = default);
    Task UpdateAsync(Coupon coupon, CancellationToken ct = default);

    /// <returns>Rows updated (1 if UsedQuota was decremented).</returns>
    Task<int> TryDecrementUsedQuotaIfPositiveAsync(string couponCode, CancellationToken ct = default);
}
