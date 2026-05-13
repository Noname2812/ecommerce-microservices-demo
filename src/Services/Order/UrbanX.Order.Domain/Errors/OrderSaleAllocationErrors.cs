using Shared.Kernel.Primitives;

namespace UrbanX.Order.Domain.Errors;

/// <summary>Sale-allocation <see cref="Error"/> values shared by Order.Domain (<see cref="OrderErrors"/>) and infrastructure.</summary>
public static class OrderSaleAllocationErrors
{
    public static readonly Error SaleQuotaExceeded =
        new("Order.SaleQuotaExceeded", "Sale quota has been exhausted for this campaign.");

    public static readonly Error SaleUserLimitExceeded =
        new("Order.SaleUserLimitExceeded", "You have reached the purchase limit for this sale campaign.");
}
