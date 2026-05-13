namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class InventoryClientOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// Aspire service discovery base URI for Inventory (e.g. http://inventory).
    /// </summary>
    public string BaseUrl { get; set; } = "http://inventory";
}
