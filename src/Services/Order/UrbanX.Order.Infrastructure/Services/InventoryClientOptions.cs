namespace UrbanX.Order.Infrastructure.Services;

public sealed class InventoryClientOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// Aspire service discovery base URI for Inventory (e.g. http://inventory).
    /// </summary>
    public string BaseUrl { get; set; } = "http://inventory";
}
