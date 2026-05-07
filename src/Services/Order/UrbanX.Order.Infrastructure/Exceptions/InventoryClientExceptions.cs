namespace UrbanX.Order.Infrastructure.Exceptions;

/// <summary>
/// Inventory reported insufficient stock for reserve (HTTP 409 OUT_OF_STOCK).
/// </summary>
public sealed class OutOfStockException : Exception
{
    public OutOfStockException(string detail)
        : base(detail)
    {
        Detail = detail;
    }

    public string Detail { get; }
}

/// <summary>
/// Client / validation error from Inventory HTTP 4xx (except reserve conflict).
/// </summary>
public sealed class InventoryValidationException : Exception
{
    public InventoryValidationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Inventory unavailable: HTTP 5xx, transport failure, or request timeout.
/// </summary>
public sealed class InventoryUnavailableException : Exception
{
    public InventoryUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
