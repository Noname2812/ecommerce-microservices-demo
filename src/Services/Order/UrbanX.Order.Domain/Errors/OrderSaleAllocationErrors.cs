using Shared.Kernel.Primitives;

namespace UrbanX.Order.Domain.Errors;

/// <summary>Shared sale-allocation errors for Order.Application and Order.Infrastructure (single source of truth for code + message).</summary>
public static class OrderSaleAllocationErrors
{
    public static readonly Error SaleQuotaExceeded =
        new("Order.SaleQuotaExceeded", "Sale quota has been exhausted for this campaign.");

    public static readonly Error SaleUserLimitExceeded =
        new("Order.SaleUserLimitExceeded", "You have reached the purchase limit for this sale campaign.");
}
