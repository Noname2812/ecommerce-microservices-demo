using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Application.Usecases.V1.Errors;

public sealed class OutOfStockError : Error
{
    public Guid ProductId { get; }
    public int Requested { get; }
    public int Available { get; }

    public OutOfStockError(Guid productId, int requested, int available)
        : base(
            "Inventory.OutOfStock",
            $"Insufficient stock for product {productId}. Requested {requested}, available {available}.")
    {
        ProductId = productId;
        Requested = requested;
        Available = available;
    }
}

public sealed class ProductNotFoundForReservationError : Error
{
    public Guid ProductId { get; }

    public ProductNotFoundForReservationError(Guid productId)
        : base("Inventory.ProductNotFoundForReservation", $"No inventory line found for product {productId}.")
    {
        ProductId = productId;
    }
}
