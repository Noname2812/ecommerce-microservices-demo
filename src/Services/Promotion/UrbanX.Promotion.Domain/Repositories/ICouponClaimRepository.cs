using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Domain.Repositories;

public interface ICouponClaimRepository
{
    Task<CouponClaim?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CouponClaim?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(CouponClaim claim, CancellationToken ct = default);
    Task UpdateAsync(CouponClaim claim, CancellationToken ct = default);
}
