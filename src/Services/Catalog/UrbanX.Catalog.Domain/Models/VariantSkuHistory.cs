using Shared.Contract.Abstractions;

namespace UrbanX.Catalog.Domain.Models;

/// <summary>Audit log when a variant SKU is changed (append-only).</summary>
public class VariantSkuHistory : BaseEntity<Guid>
{
    public Guid VariantId { get; set; }
    public string OldSku { get; set; } = null!;
    public string NewSku { get; set; } = null!;
    public Guid ChangedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ProductVariant? Variant { get; set; }
}
