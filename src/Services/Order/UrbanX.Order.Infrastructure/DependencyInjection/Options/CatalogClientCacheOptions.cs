using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

/// <summary>
/// TTL for cached catalog responses (variant info / product validation / current prices).
/// </summary>
/// <remarks>
/// Trade-off: longer TTL eliminates more HTTP fan-out under load but lets stale prices reach the
/// saga's pricing tolerance check. Default 30s aligns with typical catalog mutation cadence.
/// </remarks>
public sealed class CatalogClientCacheOptions
{
    public const string SectionName = "Order:CatalogClient:Cache";

    [Range(1, 600)]
    public int VariantTtlSeconds { get; init; } = 30;
}
