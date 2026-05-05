using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface ICouponRepository
{
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task AddAsync(Coupon coupon, CancellationToken ct = default);
    Task UpdateAsync(Coupon coupon, CancellationToken ct = default);
}
