namespace UrbanX.Order.Domain.Models;

public sealed class ProductVariantReadModel
{
    public Guid VariantId { get; set; }

    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public bool ProductIsActive { get; set; }

    public string Sku { get; set; } = null!;
    public string? VariantName { get; set; }
    public string? ImageUrl { get; set; }

    public decimal Price { get; set; }
    public bool IsActive { get; set; }

    public Guid SellerId { get; set; }
    public string SellerName { get; set; } = null!;
    public bool SellerIsActive { get; set; }

    public int RowVersion { get; set; }

    public int ProjectionVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
