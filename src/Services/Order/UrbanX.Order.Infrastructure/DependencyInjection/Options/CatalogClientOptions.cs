using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class CatalogClientOptions
{
    public const string SectionName = "Order:CatalogClient";

    [Required]
    [Url]
    public string BaseAddress { get; init; } = "";
}
