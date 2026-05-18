using System.Security.Cryptography;
using System.Text;

namespace Shared.Messaging;

/// <summary>
/// Derives a stable <see cref="Guid"/> from an input string for saga/outbox publish deduplication on retry.
/// Not a standards-compliant UUID v3/v5 — suitable only as a deterministic idempotency key.
/// </summary>
public static class DeterministicMessageId
{
    public static Guid From(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes[..16]);
    }
}
