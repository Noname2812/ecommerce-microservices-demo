using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrbanX.Payment.Application.Configuration;

namespace UrbanX.Payment.API.Filters;

public sealed class SePayWebhookAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<SePayOptions>>();
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return Results.Json(
                new { success = false, message = "SePay webhook secret is not configured" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            authHeader.Count != 1 ||
            !string.Equals(authHeader[0], $"Bearer {secret}", StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
