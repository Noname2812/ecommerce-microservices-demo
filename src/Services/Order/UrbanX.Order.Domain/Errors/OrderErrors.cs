using Shared.Kernel.Primitives;

namespace UrbanX.Order.Domain.Errors;

public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        new("ORDER_NOT_FOUND", $"Order {id} not found");

    public static readonly Error Forbidden =
        new("ORDER_FORBIDDEN", "You do not have permission to access this order");

    public static readonly Error CannotCancel =
        new("ORDER_CANNOT_CANCEL", "This order cannot be cancelled in its current status");

    public static Error PromotionInvalid(string message) =>
        new("ORDER_PROMOTION_INVALID", message);

    public static Error ProductNotFound(Guid productId) =>
        new("PRODUCT_NOT_FOUND", $"Product {productId} was not found.");

    public static Error ProductUnavailable(Guid productId) =>
        new("PRODUCT_UNAVAILABLE", $"Product {productId} is inactive.");

    public static Error ShippingNotAvailable(string city, string district) =>
        new("SHIPPING_NOT_AVAILABLE", $"Shipping is not available for {district}, {city}.");

    public static Error VariantPriceMismatch(Guid variantId, decimal currentPrice, decimal snapshotPrice) =>
        new PriceMismatchError(variantId, currentPrice, snapshotPrice);

    public static Error OutOfStock(string detail) =>
        new("INVENTORY_OUT_OF_STOCK", detail);

    public static Error InventoryUnavailable(string message) =>
        new("INVENTORY_UNAVAILABLE", message);

    public static Error CouponClaimFailed(string message) =>
        new("COUPON_CLAIM_FAILED", message);

    public static readonly Error SaleQuotaExceeded = OrderSaleAllocationErrors.SaleQuotaExceeded;

    public static readonly Error SaleUserLimitExceeded = OrderSaleAllocationErrors.SaleUserLimitExceeded;

    public static Error SaleCampaignInvalid(string reason) =>
        new("Order.SaleCampaignInvalid", reason);

    public static readonly Error SaleWindowExpired =
        new("Order.SaleWindowExpired", "Pricing snapshot has expired. Please refresh and try again.");

    public static readonly Error SalePricingUnavailable =
        new("Order.SalePricingUnavailable", "Unable to retrieve sale prices. Please try again.");

    /// <summary>
    /// Per-SKU sale price check. Shares code <c>Order.PriceMismatch</c> with <see cref="PriceMismatch"/>
    /// (server total) — both map to HTTP 409 by design (TASK-02).
    /// </summary>
    public static Error SaleLinePriceMismatch(string sku, decimal expected, decimal actual) =>
        new("Order.PriceMismatch",
            $"Price mismatch for SKU '{sku}': expected {expected:F2}, got {actual:F2}.");

    public static readonly Error GuardUnavailable =
        new("SALES_ORDER_GUARD_UNAVAILABLE", "Service temporarily unavailable, please retry");

    // Common
    public static readonly Error TooManyPendingOrders =
        new("Order.TooManyPending", "User has reached maximum pending orders");

    public static readonly Error TicketNotFound =
        new("Order.TicketNotFound", "Ticket not found");

    // Catalog
    public static Error CatalogValidationFailed(string reason) =>
        new("Order.CatalogValidationFailed", reason);

    public static readonly Error CatalogUnavailable =
        new("Order.CatalogUnavailable", "Catalog service unavailable");

    // Sales
    public static Error FlashSaleSoldOut(Guid saleId) =>
        new("Order.FlashSaleSoldOut", $"Flash sale {saleId} is sold out");

    public static readonly Error SaleExpired =
        new("Order.SaleExpired", "Flash sale has expired");

    public static readonly Error UserAlreadyBoughtFromSale =
        new("Order.UserAlreadyBoughtFromSale", "User already bought from this sale");

    /// <summary>
    /// Server-computed order total vs client expected (&gt;1% tolerance). Same code as
    /// <see cref="SaleLinePriceMismatch"/> — API cannot distinguish; both return 409 (TASK-02).
    /// </summary>
    public static readonly Error PriceMismatch =
        new("Order.PriceMismatch", "Server-calculated price differs from expected (>1%)");

    public static readonly Error CouponNotEligible =
        new("Order.CouponNotEligible", "User is not eligible for this coupon");

    public static readonly Error CouponAlreadyUsed =
        new("Order.CouponAlreadyUsed", "User has already used this coupon");

    public static readonly Error CouponExhausted =
        new("Order.CouponExhausted", "Coupon has no remaining quota");
}

public sealed class PriceMismatchError : Error
{
    public Guid VariantId { get; }
    public decimal CurrentPrice { get; }
    public decimal SnapshotPrice { get; }

    public PriceMismatchError(Guid variantId, decimal currentPrice, decimal snapshotPrice)
        : base("PRICE_MISMATCH",
            $"Price changed for variant {variantId}. Current price is {currentPrice}, snapshot price is {snapshotPrice}.")
    {
        VariantId = variantId;
        CurrentPrice = currentPrice;
        SnapshotPrice = snapshotPrice;
    }
}
