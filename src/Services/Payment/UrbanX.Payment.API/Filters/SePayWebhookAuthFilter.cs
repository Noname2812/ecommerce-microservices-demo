using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Integrations.SePay;

namespace UrbanX.Payment.API.Filters;

/// <summary>
/// Verifies inbound SePay webhook requests using HMAC-SHA256 over <c>"{timestamp}.{rawBody}"</c>.
/// Falls back to legacy <c>Authorization: Bearer &lt;secret&gt;</c> when <see cref="SePayOptions.HmacSecret"/> is empty.
/// </summary>
public sealed class SePayWebhookAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var sp = context.HttpContext.RequestServices;
        var options = sp.GetRequiredService<IOptionsSnapshot<SePayOptions>>().Value;
        var logger = sp.GetRequiredService<ILogger<SePayWebhookAuthFilter>>();

        var hmacSecret = options.HmacSecret;
        if (!string.IsNullOrWhiteSpace(hmacSecret))
        {
            return await VerifyHmacAsync(context, next, hmacSecret, options.WebhookTimestampToleranceSeconds, logger);
        }

        var bearer = options.WebhookSecret;
        if (string.IsNullOrWhiteSpace(bearer))
        {
            logger.LogError("SePay webhook reached but neither HmacSecret nor WebhookSecret is configured.");
            return Results.Json(
                new { success = false, message = "SePay webhook secret is not configured" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            authHeader.Count != 1 ||
            !string.Equals(authHeader[0], $"Bearer {bearer}", StringComparison.Ordinal))
        {
            logger.LogWarning("SePay webhook bearer authentication failed.");
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static async ValueTask<object?> VerifyHmacAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        string hmacSecret,
        int timestampToleranceSeconds,
        ILogger logger)
    {
        var request = context.HttpContext.Request;

        if (!request.Headers.TryGetValue(SePayIntegrationConstants.HmacHeaderName, out var sigHeader) ||
            sigHeader.Count != 1 ||
            string.IsNullOrWhiteSpace(sigHeader[0]))
        {
            logger.LogWarning("SePay webhook missing {Header}.", SePayIntegrationConstants.HmacHeaderName);
            return Results.Unauthorized();
        }

        if (!request.Headers.TryGetValue(SePayIntegrationConstants.TimestampHeaderName, out var tsHeader) ||
            tsHeader.Count != 1 ||
            !long.TryParse(tsHeader[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestampSeconds))
        {
            logger.LogWarning("SePay webhook missing or malformed {Header}.", SePayIntegrationConstants.TimestampHeaderName);
            return Results.Unauthorized();
        }

        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowSeconds - timestampSeconds) > timestampToleranceSeconds)
        {
            logger.LogWarning(
                "SePay webhook timestamp skew exceeded tolerance. Received={Received} Now={Now} Tolerance={ToleranceSeconds}",
                timestampSeconds, nowSeconds, timestampToleranceSeconds);
            return Results.Unauthorized();
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
        request.Body.Position = 0;

        var sigValue = sigHeader[0]!;
        var sigHex = sigValue.StartsWith(SePayIntegrationConstants.HmacHeaderPrefix, StringComparison.OrdinalIgnoreCase)
            ? sigValue[SePayIntegrationConstants.HmacHeaderPrefix.Length..]
            : sigValue;

        byte[] receivedSig;
        try
        {
            receivedSig = Convert.FromHexString(sigHex);
        }
        catch (FormatException)
        {
            logger.LogWarning("SePay webhook signature is not valid hex.");
            return Results.Unauthorized();
        }

        var signedPayload = $"{timestampSeconds}.{rawBody}";
        var computed = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(hmacSecret),
            Encoding.UTF8.GetBytes(signedPayload));

        if (!CryptographicOperations.FixedTimeEquals(receivedSig, computed))
        {
            logger.LogWarning("SePay webhook HMAC signature mismatch.");
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
