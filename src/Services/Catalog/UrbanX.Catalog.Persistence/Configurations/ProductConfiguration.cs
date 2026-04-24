using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ValueObjects;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        private static readonly JsonSerializerOptions Json = new();

        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable(TableNames.Products);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            builder.HasIndex(x => x.Sku).IsUnique();
            builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
            builder.Property(x => x.Slug).HasMaxLength(500).IsRequired();
            builder.HasIndex(x => x.Slug).IsUnique();
            builder.Property(x => x.Description);
            builder.Property(x => x.ShortDescription).HasMaxLength(500);
            builder.Property(x => x.BasePrice).HasPrecision(18, 2).IsRequired();
            builder.Property(x => x.SellerName).HasMaxLength(255).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.HasIndex(x => x.Status);
            builder.HasIndex(x => x.SellerId);
            builder.HasIndex(x => x.CategoryId);
            builder.HasIndex(x => x.DeletedAt);
            builder.Property(x => x.CategoryName).HasMaxLength(255);
            builder.Property(x => x.BrandName).HasMaxLength(255);
            builder.Property(x => x.MetaTitle).HasMaxLength(255);
            builder.Property(x => x.MetaDescription);
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.UpdatedAt).IsRequired();
            builder.Property(x => x.RowVersion).IsConcurrencyToken();
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.WeightGrams);
            builder.Property(p => p.Tags)
                .HasColumnType("text[]");

            builder.Property(p => p.Dimensions)
                .HasColumnType("jsonb")
                .HasConversion(
                    d => d == null ? null : JsonSerializer.Serialize(d, Json),
                    s => string.IsNullOrEmpty(s) ? null : JsonSerializer.Deserialize<ProductDimensions>(s, Json));

            builder
                .HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder
                .HasOne(p => p.Brand)
                .WithMany()
                .HasForeignKey(p => p.BrandId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(p => p.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
