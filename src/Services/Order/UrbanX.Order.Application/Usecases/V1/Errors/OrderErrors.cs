using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Errors;

public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        new("ORDER_NOT_FOUND", $"Order {id} not found");

    public static readonly Error Forbidden =
        new("ORDER_FORBIDDEN", "You do not have permission to access this order");

    public static readonly Error CannotCancel =
        new("ORDER_CANNOT_CANCEL", "This order cannot be cancelled in its current status");

    public static readonly Error AlreadyExists =
        new("ORDER_ALREADY_EXISTS", "An order with this idempotency key already exists");

    public static Error PromotionInvalid(string message) =>
        new("ORDER_PROMOTION_INVALID", message);

    public static Error ProductNotFound(Guid productId) =>
        new("PRODUCT_NOT_FOUND", $"Product {productId} was not found.");

    public static Error ProductUnavailable(Guid productId) =>
        new("PRODUCT_UNAVAILABLE", $"Product {productId} is inactive.");

    public static Error ShippingNotAvailable(string city, string district) =>
        new("SHIPPING_NOT_AVAILABLE", $"Shipping is not available for {district}, {city}.");

    public static Error PriceMismatch(Guid variantId, decimal currentPrice, decimal snapshotPrice) =>
        new PriceMismatchError(variantId, currentPrice, snapshotPrice);
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
