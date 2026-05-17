namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

internal static class OrderNumberGenerator
{
    public static string Generate(string prefix)
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"{prefix}-{date}-{suffix}";
    }
}
