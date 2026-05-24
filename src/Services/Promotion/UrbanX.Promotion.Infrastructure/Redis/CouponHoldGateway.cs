using Shared.Cache.Abstractions;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Domain.Constants;

namespace UrbanX.Promotion.Infrastructure.Redis;

internal sealed class CouponHoldGateway(ICacheService cache) : ICouponHoldGateway
{
    public Task SetHoldAsync(string token, CouponHoldInfo info, TimeSpan ttl, CancellationToken ct = default) =>
        cache.SetAsync(CouponRedisKeys.Hold(token), info, ttl, ct);

    public Task<CouponHoldInfo?> TryGetAsync(string token, CancellationToken ct = default) =>
        cache.GetAsync<CouponHoldInfo>(CouponRedisKeys.Hold(token), ct);

    public async Task<bool> TryDeleteAsync(string token, CancellationToken ct = default)
    {
        var key = CouponRedisKeys.Hold(token);
        var existed = await cache.ExistsAsync(key, ct);
        if (!existed)
            return false;

        await cache.RemoveAsync(key, ct);
        return true;
    }
}
