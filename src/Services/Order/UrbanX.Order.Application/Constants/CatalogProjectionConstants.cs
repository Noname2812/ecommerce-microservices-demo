namespace UrbanX.Order.Application.Constants;

/// <summary>
/// Single source of truth for hard-coded identifiers used by the tiered Catalog validation pipeline:
/// cache keys, meter / metric names, and metric tag keys/values.
/// Error codes live in <c>OrderErrors</c> (Domain layer).
/// </summary>
public static class CatalogProjectionConstants
{
    public static class CacheKeys
    {
        public const string ProductPrefix = "order:catalog:product:";
        public const string VariantPrefix = "order:catalog:variant:";

        public static string Product(Guid productId) => $"{ProductPrefix}{productId}";
        public static string Variant(Guid variantId) => $"{VariantPrefix}{variantId}";
    }

    public static class Metrics
    {
        public const string MeterName = "UrbanX.Order";
        public const string MeterVersion = "1.0.0";

        public const string ValidatorSourceCounter = "order.validator.source";
        public const string ValidatorDurationHistogram = "order.validator.duration_ms";
    }

    public static class Tags
    {
        public const string Validator = "validator";
        public const string Source = "source";
    }

    public static class ValidatorNames
    {
        public const string Product = "product";
        public const string Pricing = "pricing";
    }

    public static class Sources
    {
        public const string CacheHit = "cache_hit";
        public const string LocalHit = "local_hit";
        public const string HttpFallback = "http_fallback";
        public const string Failed = "failed";
    }
}
