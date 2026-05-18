namespace UrbanX.Catalog.Domain.Models;

/// <summary>Variant row with parent product fields for internal batch reads (Order saga).</summary>
public sealed record VariantCatalogSnapshot(
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    Guid VariantId,
    string Sku,
    string? VariantName,
    bool VariantIsActive,
    decimal CurrentPrice,
    Guid SellerId,
    string SellerName,
    /// <summary>
    /// No dedicated seller-active column in catalog schema; mirrors <see cref="ProductIsActive"/>
    /// until Merchant/Identity exposes seller suspension state.
    /// </summary>
    bool SellerIsActive,
    string? ImageUrl);
