using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class PlaceOrderOptions
{
    public const string SectionName = "PlaceOrder";

    [Range(1, 100)]
    public int MaxNormalPendingPerUser { get; init; } = 1;

    [Range(1, 100)]
    public int MaxSalesPendingPerUser { get; init; } = 3;

    [Range(1, 1440)]
    public int PendingSlotTtlMinutes { get; init; } = 30;

    [Range(60, 86400)]
    public int CouponLockTtlSeconds { get; init; } = 960;

    /// <summary>
    /// Acceptable relative drift between client `ExpectedTotal` and server-computed `FinalTotal`.
    /// E.g. <c>0.01</c> = 1%. Must be in [0, 1].
    /// </summary>
    [Range(typeof(decimal), "0", "1")]
    public decimal PriceMismatchTolerance { get; init; } = 0.01m;
}
