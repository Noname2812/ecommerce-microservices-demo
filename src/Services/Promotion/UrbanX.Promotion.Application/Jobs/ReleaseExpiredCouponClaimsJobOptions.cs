using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Application.Jobs;

public sealed class ReleaseExpiredCouponClaimsJobOptions
{
    public const string SectionName = "Promotion:Jobs:ReleaseExpiredCouponClaims";

    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 200;

    [Required]
    public string CronExpression { get; set; } = "*/2 * * * *";
}
