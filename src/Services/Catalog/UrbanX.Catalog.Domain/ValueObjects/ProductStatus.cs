namespace UrbanX.Catalog.Domain.ValueObjects
{
    /// <summary>Maps to products.status: DRAFT, ACTIVE, INACTIVE, or DELETED.</summary>
    public static class ProductStatus
    {
        public const string Draft = "DRAFT";
        public const string Active = "ACTIVE";
        public const string Inactive = "INACTIVE";
        public const string Deleted = "DELETED";
    }
}
