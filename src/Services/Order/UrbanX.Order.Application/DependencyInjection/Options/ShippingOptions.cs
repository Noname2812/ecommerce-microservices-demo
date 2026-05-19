namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class ShippingOptions
{
    public const string SectionName = "Shipping";

    public List<string> SupportedRegions { get; set; } = [];
}
