namespace Shared.Messaging.Idempotency;

public static class IdempotencyHttpConstants
{
    /// <summary>RFC-style idempotency header for HTTP write operations.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public const string MissingKeyType = "MISSING_IDEMPOTENCY_KEY";

    public const string InvalidKeyType = "INVALID_IDEMPOTENCY_KEY";
}
