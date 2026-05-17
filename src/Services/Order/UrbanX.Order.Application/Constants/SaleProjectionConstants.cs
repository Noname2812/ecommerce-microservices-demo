namespace UrbanX.Order.Application.Constants;

/// <summary>
/// Cache keys / metric tags for sale-snapshot tiered lookup (memory + Redis).
/// Mirrors <see cref="CatalogProjectionConstants"/>.
/// </summary>
public static class SaleProjectionConstants
{
    public static class CacheKeys
    {
        public static string CampaignMeta(Guid campaignId) => $"sale:{campaignId}:meta";
        public static string CampaignPrices(Guid campaignId) => $"sale:{campaignId}:prices";
    }

    public static class ValidatorNames
    {
        public const string SaleEligibility = "sale_eligibility";
        public const string SalePricing     = "sale_pricing";
    }

    public static class Sources
    {
        public const string MemoryHit = "memory_hit";
        public const string RedisHit  = "redis_hit";
        public const string Miss      = "miss";
    }
}
