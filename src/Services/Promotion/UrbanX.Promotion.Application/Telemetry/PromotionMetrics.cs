using System.Diagnostics.Metrics;

namespace UrbanX.Promotion.Application.Telemetry;

/// <summary>OpenTelemetry meter for Promotion (register with <see cref="MeterName"/> in the host).</summary>
public static class PromotionMetrics
{
    public const string MeterName = "UrbanX.Promotion";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Redis cleanup failed after the claim row was committed as RELEASED.</summary>
    public static readonly Counter<long> CouponClaimRedisPostCommitFailures = Meter.CreateCounter<long>(
        "coupon_claim.redis_post_commit_failures",
        unit: "{failure}",
        description: "Coupon claim Redis cleanup failed after SQL commit (possible stale user hold / quota; alert or reconcile)");

    public static void RecordCouponClaimRedisPostCommitFailure(
        Guid claimId,
        string couponCode,
        Guid userId,
        bool restoreQuotaSlotOnRelease) =>
        CouponClaimRedisPostCommitFailures.Add(
            1,
            new KeyValuePair<string, object?>("claim_id", claimId.ToString("D")),
            new KeyValuePair<string, object?>("coupon_code", couponCode),
            new KeyValuePair<string, object?>("user_id", userId.ToString("D")),
            new KeyValuePair<string, object?>("restore_quota_slot", restoreQuotaSlotOnRelease ? "true" : "false"));
}
