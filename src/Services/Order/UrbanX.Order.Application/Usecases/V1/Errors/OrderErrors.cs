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
}
