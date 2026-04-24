using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Shared.Kernel.Constants;
using UrbanX.Gateway.Application.Constants;

namespace UrbanX.Gateway.Infrastructure.Error;

public static class GatewayErrorResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(
        HttpContext http,
        int status,
        string error,
        string message,
        string? requestId = null,
        int? retryAfter = null,
        bool includeRateLimitBodyFields = false,
        CancellationToken cancellationToken = default)
    {
        if (http.Response.HasStarted)
        {
            return;
        }

        http.Response.StatusCode = status;
        http.Response.ContentType = "application/json; charset=utf-8";

        var id = !string.IsNullOrEmpty(requestId) ? requestId! : (http.Request.Headers[GatewayHeaderNames.XRequestId].ToString() ?? http.TraceIdentifier);

        if (status == 429)
        {
            if (includeRateLimitBodyFields)
            {
                var sec = retryAfter is > 0 ? retryAfter.Value : 60;
                http.Response.Headers["Retry-After"] = sec.ToString();
            }
            else if (retryAfter is > 0)
            {
                http.Response.Headers["Retry-After"] = retryAfter.ToString()!;
            }

            if (!http.Response.Headers.ContainsKey("X-Request-Id"))
            {
                http.Response.Headers[GatewayHeaderNames.XRequestId] = id;
            }
        }
        else if (!http.Response.Headers.ContainsKey("X-Request-Id") && (status is 400 or 401 or 403 or 404 or 500))
        {
            http.Response.Headers[GatewayHeaderNames.XRequestId] = id;
        }

        var ts = DateTimeOffset.UtcNow.ToString("o");
        object body;
        if (status == StatusCodes.Status429TooManyRequests && includeRateLimitBodyFields)
        {
            var r = retryAfter is > 0 ? retryAfter.Value : 60;
            body = new
            {
                request_id = id,
                timestamp = ts,
                error,
                message,
                details = (string?)null,
                retry_after = r
            };
        }
        else
        {
            body = new
            {
                request_id = id,
                timestamp = ts,
                error,
                message,
                details = (string?)null
            };
        }

        await http.Response.WriteAsync(
            JsonSerializer.Serialize(body, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }
}
