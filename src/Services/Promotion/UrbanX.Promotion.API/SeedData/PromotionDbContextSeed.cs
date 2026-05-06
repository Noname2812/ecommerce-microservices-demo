using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using UrbanX.Promotion.Domain.Constants;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.ValueObjects;
using UrbanX.Promotion.Persistence;

namespace UrbanX.Promotion.API.SeedData;

public static class PromotionDbContextSeed
{
    public static async Task SeedCouponsAsync(
        PromotionDbContext db,
        IConnectionMultiplexer redis,
        ILogger logger)
    {
        if (await db.Coupons.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;

        var coupons = new List<Coupon>
        {
            // 1. Unlimited quota — percentage discount, always valid
            new() { Id = "WELCOME10", DiscountType = DiscountType.Percentage, DiscountValue = 10, TotalQuota = null, UsedQuota = 0, MinOrderValue = 0, ValidFrom = now.AddDays(-30), ExpiresAt = now.AddYears(1), IsActive = true },

            // 2. Limited quota (100 uses), partially used — fixed amount
            new() { Id = "SAVE50K", DiscountType = DiscountType.FixedAmount, DiscountValue = 50_000, TotalQuota = 100, UsedQuota = 12, MinOrderValue = 200_000, ValidFrom = now.AddDays(-7), ExpiresAt = now.AddDays(30), IsActive = true },

            // 3. Exhausted + expired coupon
            new() { Id = "FLASH30", DiscountType = DiscountType.Percentage, DiscountValue = 30, TotalQuota = 200, UsedQuota = 200, MinOrderValue = 0, ValidFrom = now.AddDays(-60), ExpiresAt = now.AddDays(-1), IsActive = false },

            // 4. Future coupon — not yet active
            new() { Id = "SUMMER25", DiscountType = DiscountType.Percentage, DiscountValue = 25, TotalQuota = 500, UsedQuota = 0, MinOrderValue = 300_000, ValidFrom = now.AddDays(7), ExpiresAt = now.AddDays(37), IsActive = true },

            // 5. Free shipping — unlimited, active
            new() { Id = "FREESHIP", DiscountType = DiscountType.FreeShipping, DiscountValue = 0, TotalQuota = null, UsedQuota = 0, MinOrderValue = 150_000, ValidFrom = now.AddDays(-1), ExpiresAt = now.AddMonths(3), IsActive = true }
        };

        db.Coupons.AddRange(coupons);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} coupons", coupons.Count);

        // Sync remaining quota to Redis.
        // Key: coupon:{code}:quota stores REMAINING quota (countdown).
        // Claim handler uses DECR; result < 0 means exhausted → rollback and deny.
        var redisDb = redis.GetDatabase();
        foreach (var coupon in coupons.Where(c => c.TotalQuota.HasValue))
        {
            var remaining = coupon.TotalQuota!.Value - coupon.UsedQuota;
            var key = CouponRedisKeys.Quota(coupon.Id);
            await redisDb.StringSetAsync(key, remaining);
            logger.LogInformation("Redis quota synced: {Key} = {Remaining}", key, remaining);
        }
    }
}
