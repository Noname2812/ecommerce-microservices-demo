using Shared.Kernel.Domain;

namespace UrbanX.Order.Domain.Models;

public sealed class OrderItem : BaseEntity<Guid>
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public string? ProductSlug { get; private set; }
    public Guid VariantId { get; private set; }
    public string VariantSku { get; private set; } = null!;
    public string? VariantName { get; private set; }
    public Guid SellerId { get; private set; }
    public string SellerName { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal Subtotal { get; private set; }
    public string? ImageUrl { get; private set; }
    public string Status { get; private set; } = OrderStatus.Processing;
    public int RefundedQuantity { get; private set; }

    private OrderItem() { }

    internal static OrderItem Create(
        Guid orderId,
        Guid productId,
        string productName,
        string? productSlug,
        Guid variantId,
        string variantSku,
        string? variantName,
        Guid sellerId,
        string sellerName,
        decimal unitPrice,
        int quantity,
        decimal discountAmount,
        string? imageUrl) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductId = productId,
        ProductName = productName,
        ProductSlug = productSlug,
        VariantId = variantId,
        VariantSku = variantSku,
        VariantName = variantName,
        SellerId = sellerId,
        SellerName = sellerName,
        UnitPrice = unitPrice,
        Quantity = quantity,
        DiscountAmount = discountAmount,
        Subtotal = unitPrice * quantity - discountAmount,
        ImageUrl = imageUrl,
        Status = OrderStatus.Processing,
        RefundedQuantity = 0
    };
}
