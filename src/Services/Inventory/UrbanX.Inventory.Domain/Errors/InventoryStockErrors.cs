using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Domain.Errors;

public static class InventoryStockErrors
{
    public static readonly Error InvalidReleaseQuantity =
        new("InventoryItem.InvalidReleaseQuantity", "Release quantity must be greater than zero.");

    public static readonly Error InvalidConfirmQuantity =
        new("InventoryItem.InvalidConfirmQuantity", "Confirm quantity must be positive.");

    public static Error InsufficientReservedForConfirm(Guid inventoryItemId, int confirmQty, int quantityReserved) =>
        new(
            "InventoryItem.InsufficientReservedForConfirm",
            $"Cannot confirm {confirmQty} from item {inventoryItemId}; only {quantityReserved} reserved.");

    public static Error InsufficientReservedForRelease(Guid inventoryItemId, int releaseQty, int quantityReserved) =>
        new(
            "InventoryItem.InsufficientReservedForRelease",
            $"Cannot release {releaseQty} from item {inventoryItemId}; only {quantityReserved} reserved.");
}
