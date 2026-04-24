namespace UrbanX.Gateway.Application.Constants;

/// <summary>Stable error code strings returned in gateway-generated JSON (before upstream response).</summary>
public static class GatewayErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string RouteNotFound = "ROUTE_NOT_FOUND";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string UpstreamError = "UPSTREAM_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string GatewayTimeout = "GATEWAY_TIMEOUT";
}
