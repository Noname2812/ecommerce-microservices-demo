using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
    {
        public void Configure(EntityTypeBuilder<ProductVariant> builder)
        {
            builder.ToTable(TableNames.ProductVariants);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => x.Sku).IsUnique();
            builder.HasIndex(x => x.ProductId);
            builder.Property(x => x.Name).HasMaxLength(255);
            builder.Property(x => x.Price).HasPrecision(18, 2).IsRequired();
            builder.Property(x => x.CompareAtPrice).HasPrecision(18, 2);
            builder.Property(x => x.ImageUrl).HasMaxLength(500);
            builder.Property(x => x.Barcode).HasMaxLength(100);
            builder.Property(x => x.IsActive).HasDefaultValue(true);
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.RowVersion).IsConcurrencyToken();
            builder.Property(x => x.DeletedAt);
            builder.HasIndex(x => new { x.ProductId, x.IsActive });
        }
    }
}
