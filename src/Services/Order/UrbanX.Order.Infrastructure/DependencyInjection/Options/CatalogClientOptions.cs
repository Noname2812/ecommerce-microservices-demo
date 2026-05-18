using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

public sealed class CatalogClientOptions
{
    public const string SectionName = "Order:CatalogClient";

    [Required]
    [Url]
    public string BaseAddress { get; init; } = "";

    [Range(100, 120_000)]
    public int TimeoutMilliseconds { get; init; } = 3_000;
}
