using Microsoft.Extensions.Logging;

namespace UrbanX.Promotion.Application.Logging;

/// <summary>Stable ids for alerting / log queries (Promotion service).</summary>
public static class PromotionLogEvents
{
    public static readonly EventId CouponClaimRedisPostCommitFailed =
        new(65101, nameof(CouponClaimRedisPostCommitFailed));

    public static readonly EventId PromotionPostCommitBatchFailed =
        new(65102, nameof(PromotionPostCommitBatchFailed));
}
