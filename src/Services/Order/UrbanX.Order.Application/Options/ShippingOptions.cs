namespace UrbanX.Order.Application.Options;

public sealed class ShippingOptions
{
    public const string SectionName = "Shipping";

    public List<string> SupportedRegions { get; set; } = [];
}
