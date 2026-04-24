using Shared.Contract.Abstractions;
using UrbanX.Catalog.Domain.Exceptions;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Domain.Models
{
    public class ProductVariant : BaseEntity<Guid>
    {
        public Guid ProductId { get; private set; }
        public string Sku { get; private set; } = null!;
        public string? Name { get; private set; }
        public decimal Price { get; private set; }
        public decimal? CompareAtPrice { get; private set; }
        public string? ImageUrl { get; private set; }
        public string? Barcode { get; private set; }
        public bool IsActive { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public int RowVersion { get; set; } = 1;
        public DateTimeOffset? DeletedAt { get; set; }

        public Product Product { get; set; } = null!;
        public ICollection<VariantAttributeValue> AttributeValues { get; private set; } = new List<VariantAttributeValue>();

        private ProductVariant() { } // EF

        public bool IsDeleted() => DeletedAt is not null;

        public static ProductVariant Create(
            Guid productId,
            string sku,
            string? name,
            decimal price,
            decimal? compareAtPrice,
            string? imageUrl,
            string? barcode,
            IReadOnlyList<(Guid AttributeId, string Value)> attributeValues)
        {
            if (string.IsNullOrWhiteSpace(sku))
                throw new ProductExceptions.VariantSkuRequired();
            var id = Guid.NewGuid();
            var v = new ProductVariant
            {
                Id = id,
                ProductId = productId,
                Sku = sku,
                Name = name,
                Price = price,
                CompareAtPrice = compareAtPrice,
                ImageUrl = imageUrl,
                Barcode = barcode,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 1
            };

            foreach (var (defId, val) in attributeValues)
            {
                v.AttributeValues.Add(new VariantAttributeValue
                {
                    VariantId = id,
                    AttributeId = defId,
                    Value = val
                });
            }

            return v;
        }

        public void SetSku(string sku) => Sku = sku ?? throw new ArgumentNullException(nameof(sku));

        public void SetName(string? name) => Name = name;

        public void SetPrice(decimal price, decimal? compareAtPrice)
        {
            if (price <= 0)
                throw new ProductExceptions.InvalidPrice();
            Price = price;
            CompareAtPrice = compareAtPrice;
        }

        public void SetImageUrl(string? u) => ImageUrl = u;

        public void SetIsActive(bool active) => IsActive = active;

        public void MarkSoftDeleted(DateTimeOffset at)
        {
            DeletedAt = at;
            IsActive = false;
        }
    }
}
