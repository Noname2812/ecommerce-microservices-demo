using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Services;

public interface ICouponLockService
{
    Task<Result<CouponLockInfo>> TryLockAsync(string couponCode, Guid userId, CancellationToken ct);

    Task ReleaseAsync(string couponCode, Guid userId, CancellationToken ct);

    Task ConfirmUseAsync(string couponCode, Guid userId, CancellationToken ct);
}

public sealed record CouponLockInfo(decimal DiscountAmount, string DiscountType);
