using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models;

/// <summary>Audit log when a variant's price is changed (append-only).</summary>
public class VariantPriceHistory : BaseEntity<Guid>
{
    public Guid VariantId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? OldCompareAt { get; set; }
    public decimal? NewCompareAt { get; set; }
    public Guid ChangedById { get; set; }
    public string ChangedByName { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ProductVariant? Variant { get; set; }
}
