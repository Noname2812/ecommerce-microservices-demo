namespace Shared.Security.Gateway;

/// <summary>Headers the gateway may add, strip, or forward (see API gateway design).</summary>
public static class GatewayHeaderNames
{
    public const string XRequestId = "X-Request-Id";
    public const string XUserId = "X-User-Id";
    public const string XUserRoles = "X-User-Roles";
    public const string XMerchantId = "X-Merchant-Id";
    public const string XPermissionScope = "X-Permission-Scope";
    public const string XForwardedFor = "X-Forwarded-For";
    public const string XForwardedHost = "X-Forwarded-Host";
    public const string XRateLimitLimit = "X-RateLimit-Limit";
    public const string XRateLimitRemaining = "X-RateLimit-Remaining";
    public const string XRateLimitReset = "X-RateLimit-Reset";
    public const string Authorization = "Authorization";
    public const string Cookie = "Cookie";
    public const string TraceParent = "traceparent";
    public const string RequestId = "request_id";
    public const string Correlation = "X-Correlation-Id";
}
