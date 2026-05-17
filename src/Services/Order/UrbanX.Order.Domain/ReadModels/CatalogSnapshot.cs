namespace UrbanX.Order.Domain.ReadModels;

/// <summary>
/// Denormalized projection of Catalog product/variant state, maintained via integration events.
/// Validators (ProductValidator, PricingValidator) read this as Tier 2 fallback before HTTP.
/// Source of truth remains Catalog service; this is eventual-consistent within a few seconds.
/// </summary>
public sealed class CatalogSnapshot
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = null!;
    public bool ProductIsActive { get; set; }
    public bool VariantIsActive { get; set; }
    public decimal CurrentPrice { get; set; }
    public long ProjectionVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
}
