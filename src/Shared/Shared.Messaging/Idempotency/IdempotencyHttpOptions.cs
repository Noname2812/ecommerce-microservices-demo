using Microsoft.AspNetCore.Http;

namespace Shared.Messaging.Idempotency;

public sealed class IdempotencyHttpOptions
{
    public const string SectionName = "Shared:HttpIdempotency";

    /// <summary>
    /// Short name used in the Redis key suffix (<c>idempotency:{{key}}:{{ServiceId}}</c>) so different services never collide.
    /// </summary>
    public string ServiceId { get; set; } = "";

    /// <summary>
    /// When <c>null</c>, the default only applies to <c>POST</c>, <c>PUT</c>, and <c>PATCH</c> under <c>/api</c>
    /// (idempotent methods like <c>GET</c> are not required to send a key).
    /// Return <c>false</c> to skip this middleware for a request without validating the header.
    /// </summary>
    public Func<HttpContext, bool>? ShouldApply { get; set; }
}
