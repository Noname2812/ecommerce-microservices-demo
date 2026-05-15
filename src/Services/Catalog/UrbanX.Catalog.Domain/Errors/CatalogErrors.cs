using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Domain.Errors;

public static class CatalogErrors
{
    public static Error ProductNotFound(Guid id) => new("PRODUCT_NOT_FOUND", $"Product {id} not found");

    public static Error CategoryNotFound(Guid id) =>
        new("CATEGORY_NOT_FOUND", $"Category {id} was not found");

    public static Error BrandNotFound(Guid id) =>
        new("BRAND_NOT_FOUND", $"Brand {id} was not found");

    public static Error Forbidden() => new("FORBIDDEN", "You are not allowed to change this product.");
    public static Error OptimisticLock(int? current) =>
        new("OPTIMISTIC_LOCK_CONFLICT", "The product was changed by another user. Please refresh.");
    public static Error VariantLock(Guid variantId) =>
        new("OPTIMISTIC_LOCK_CONFLICT", $"Variant {variantId} was changed by another user. Please refresh.");
    public static Error SkuExists(string sku) => new("SKU_ALREADY_EXISTS", $"The SKU \"{sku}\" is already in use");
    public static Error SlugExists(string slug) => new("SLUG_ALREADY_EXISTS", $"The slug \"{slug}\" is already in use");
    public static Error VariantNotFound(Guid id) => new("VARIANT_NOT_FOUND", $"Variant {id} not found");
    public static Error AttributeCombination() =>
        new("VARIANT_ATTRIBUTE_COMBINATION_EXISTS", "A variant with the same attribute values already exists.");
    public static Error VariantHasActiveReservations() =>
        new("VARIANT_HAS_ACTIVE_ORDERS", "Cannot remove this variant while reservations exist. Disable it instead.");
    public static Error ProductHasActiveOrders() =>
        new("PRODUCT_HAS_ACTIVE_ORDERS", "Cannot delete the product while orders are still in progress.");
    public static Error InventoryCheckUnavailable() =>
        new("INVENTORY_CHECK_UNAVAILABLE", "Cannot confirm reservation state. Please try again later.");
    public static Error NoActiveVariant() =>
        new("NO_ACTIVE_VARIANT", "Snapshot must contain at least one active variant.");

    public static Error InvalidCursor(string cursor) =>
        new("INVALID_CURSOR", $"Pagination cursor is invalid or has been tampered with: '{cursor}'");
}
