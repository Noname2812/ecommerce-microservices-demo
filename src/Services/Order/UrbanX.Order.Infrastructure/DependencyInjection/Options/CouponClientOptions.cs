namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class CouponClientOptions
{
    public const string SectionName = "Coupon";

    /// <summary>Aspire/service discovery base URI for Promotion (coupon claim APIs).</summary>
    public string BaseUrl { get; set; } = "http://promotion";
}
