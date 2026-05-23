namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class ShippingOptions
{
    public const string SectionName = "Shipping";

    public List<string> SupportedRegions { get; set; } = [];
}
