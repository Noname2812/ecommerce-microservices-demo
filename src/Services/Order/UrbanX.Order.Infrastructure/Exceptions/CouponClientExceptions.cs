namespace UrbanX.Order.Infrastructure.Exceptions;

/// <summary>
/// Coupon claim conflict (quota / already used) — HTTP 409 + problem type coupon code.
/// </summary>
public sealed class CouponException : Exception
{
    public CouponException(string type, string detail)
        : base(detail)
    {
        ErrorType = type;
        Detail = detail;
    }

    public string ErrorType { get; }
    public string Detail { get; }
}

/// <summary>
/// Business / validation rejection from Promotion coupon claim API (typically HTTP 422, may include 400).
/// </summary>
public sealed class CouponValidationException : Exception
{
    public CouponValidationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Promotion unreachable: HTTP 5xx, transport failure, or HTTP timeout.
/// </summary>
public sealed class CouponUnavailableException : Exception
{
    public CouponUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
