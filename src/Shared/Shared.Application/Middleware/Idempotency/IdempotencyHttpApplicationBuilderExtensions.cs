using Microsoft.AspNetCore.Builder;

namespace Shared.Messaging.Idempotency;

public static class IdempotencyHttpApplicationBuilderExtensions
{
    public static IApplicationBuilder UseHttpIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyHttpMiddleware>();
}
