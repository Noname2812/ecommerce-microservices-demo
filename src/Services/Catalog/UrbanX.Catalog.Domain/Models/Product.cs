using Shared.Contract.Abstractions;
using UrbanX.Catalog.Domain.Exceptions;
using UrbanX.Catalog.Domain.Helpers;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Domain.Models
{
    public class Product : BaseEntity<Guid>
    {
        public string Sku { get; private set; } = null!;
        public string Name { get; private set; } = null!;
        public string Slug { get; private set; } = null!;
        public string? Description { get; private set; }
        public string? ShortDescription { get; private set; }
        public Guid? CategoryId { get; private set; }
        public Guid? BrandId { get; private set; }
        public string? CategoryName { get; private set; }
        public string? BrandName { get; private set; }
        public decimal BasePrice { get; private set; }
        public Guid SellerId { get; private set; }
        public string SellerName { get; private set; } = null!;
        public string Status { get; private set; } = ProductStatus.Draft;
        public int? WeightGrams { get; private set; }
        public ProductDimensions? Dimensions { get; private set; }
        public List<string> Tags { get; private set; } = new();
        public string? MetaTitle { get; private set; }
        public string? MetaDescription { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public DateTimeOffset UpdatedAt { get; private set; }
        public int RowVersion { get; set; } = 1;
        public DateTimeOffset? DeletedAt { get; set; }

        public List<ProductVariant> Variants { get; private set; } = new();
        public List<ProductImage> Images { get; private set; } = new();

        public Category? Category { get; set; }
        public Brand? Brand { get; set; }

        private Product() { } // EF

        public static Product Create(
            string sku,
            string name,
            string slug,
            string? description,
            string? shortDescription,
            Guid? categoryId,
            Guid? brandId,
            string? categoryName,
            string? brandName,
            decimal basePrice,
            Guid sellerId,
            string sellerName,
            string status,
            int? weightGrams,
            ProductDimensions? dimensions,
            IReadOnlyList<string> tags,
            string? metaTitle,
            string? metaDescription,
            IReadOnlyList<NewProductImageSpec> productImages,
            IReadOnlyList<NewVariantSpec> variantSpecs)
        {
            if (string.IsNullOrWhiteSpace(sku))
                throw new ProductExceptions.SkuIsRequired();
            if (string.IsNullOrWhiteSpace(name))
                throw new ProductExceptions.ProductNameIsRequired();
            if (sellerId == Guid.Empty)
                throw new ProductExceptions.SellerIdIsRequired();
            if (string.IsNullOrWhiteSpace(sellerName))
                throw new ProductExceptions.SellerNameIsRequired();
            if (variantSpecs.Count == 0)
                throw new ProductExceptions.VariantsAreRequired();
            if (basePrice < 0)
                throw new ProductExceptions.InvalidBasePrice();

            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var product = new Product
            {
                Id = id,
                Sku = sku,
                Name = name.Trim(),
                Slug = slug,
                Description = description,
                ShortDescription = shortDescription,
                CategoryId = categoryId,
                BrandId = brandId,
                CategoryName = categoryName,
                BrandName = brandName,
                BasePrice = basePrice,
                SellerId = sellerId,
                SellerName = sellerName.Trim(),
                Status = string.IsNullOrWhiteSpace(status) ? ProductStatus.Draft : status,
                WeightGrams = weightGrams,
                Dimensions = dimensions,
                Tags = tags is { Count: > 0 } ? new List<string>(tags) : new List<string>(),
                MetaTitle = metaTitle,
                MetaDescription = metaDescription,
                CreatedAt = now,
                UpdatedAt = now,
                RowVersion = 1
            };

            var order = 0;
            foreach (var p in productImages)
            {
                product.Images.Add(new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = id,
                    VariantId = null,
                    Url = p.Url,
                    AltText = p.AltText,
                    DisplayOrder = p.DisplayOrder != 0 ? p.DisplayOrder : order++,
                    IsPrimary = p.IsPrimary
                });
            }

            foreach (var spec in variantSpecs)
            {
                var v = ProductVariant.Create(
                    id,
                    spec.Sku,
                    spec.Name,
                    spec.Price,
                    spec.CompareAtPrice,
                    spec.ImageUrl,
                    spec.Barcode,
                    spec.AttributeValues
                );
                product.Variants.Add(v);
                var vo = 0;
                foreach (var g in spec.GalleryImages)
                {
                    product.Images.Add(new ProductImage
                    {
                        Id = Guid.NewGuid(),
                        ProductId = id,
                        VariantId = v.Id,
                        Url = g.Url,
                        AltText = g.AltText,
                        DisplayOrder = g.DisplayOrder != 0 ? g.DisplayOrder : vo++,
                        IsPrimary = g.IsPrimary
                    });
                }
            }

            return product;
        }

        public void ApplyEdit(ProductEditState s, DateTimeOffset utc)
        {
            Name = s.Name.Trim();
            Slug = s.Slug.Trim().ToLowerInvariant();
            Description = s.Description;
            ShortDescription = s.ShortDescription;
            CategoryId = s.CategoryId;
            CategoryName = s.CategoryName;
            BrandId = s.BrandId;
            BrandName = s.BrandName;
            BasePrice = s.BasePrice;
            Tags = s.Tags.Count > 0 ? new List<string>(s.Tags) : new();
            WeightGrams = s.WeightGrams;
            Dimensions = s.Dimensions;
            MetaTitle = s.MetaTitle;
            MetaDescription = s.MetaDescription;
            if (!string.IsNullOrWhiteSpace(s.Status))
                Status = s.Status!;
            UpdatedAt = utc;
        }

        /// <summary>Soft delete product (status DELETED + deleted timestamp).</summary>
        public void MarkAsDeleted(DateTimeOffset deletedAt, DateTimeOffset updateAt)
        {
            Status = ProductStatus.Deleted;
            DeletedAt = deletedAt;
            UpdatedAt = updateAt;
        }
    }
}
