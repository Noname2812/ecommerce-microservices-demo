using System.Security.Cryptography;
using System.Text;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo;

public static class MomoSignature
{
    /// <summary>
    /// HMAC-SHA256 hex (lowercase) over the canonical string built from the alphabetical
    /// concatenation of <c>key=value</c> pairs joined by <c>&amp;</c>.
    /// </summary>
    public static string Compute(IDictionary<string, string?> fields, string secretKey)
    {
        var canonical = BuildCanonical(fields);
        return HmacHex(canonical, secretKey);
    }

    public static bool Verify(IDictionary<string, string?> fields, string secretKey, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(providedSignature))
            return false;

        var expected = Compute(fields, secretKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(providedSignature.Trim()));
    }

    private static string BuildCanonical(IDictionary<string, string?> fields)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var key in fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!first) sb.Append('&');
            sb.Append(key).Append('=').Append(fields[key] ?? string.Empty);
            first = false;
        }
        return sb.ToString();
    }

    private static string HmacHex(string message, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
