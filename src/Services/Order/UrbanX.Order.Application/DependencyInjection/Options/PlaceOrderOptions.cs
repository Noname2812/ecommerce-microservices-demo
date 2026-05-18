namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class PlaceOrderOptions
{
    public const string SectionName = "PlaceOrder";

    public int MaxNormalPendingPerUser { get; init; } = 1;
    public int MaxSalesPendingPerUser { get; init; } = 3;
    public int PendingSlotTtlMinutes { get; init; } = 30;
    public int CouponLockTtlSeconds { get; init; } = 960;
    public decimal PriceMismatchTolerance { get; init; } = 0.01m;
}
