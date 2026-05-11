using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.ReadModels;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations;

internal sealed class ProductDetailViewConfiguration : IEntityTypeConfiguration<ProductDetailView>
{
    public void Configure(EntityTypeBuilder<ProductDetailView> builder)
    {
        builder.ToTable(TableNames.ProductDetailView, TableNames.ReadSchema);
        builder.HasKey(x => x.ProductId);
        builder.Property(x => x.ProductId).ValueGeneratedNever();
        builder.Property(x => x.SellerId).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CategoryName).HasMaxLength(255);
        builder.Property(x => x.BrandName).HasMaxLength(255);
        builder.Property(x => x.ShortDescription).HasMaxLength(500);
        builder.Property(x => x.BasePrice).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PrimaryImageUrl).HasMaxLength(500);
        builder.Property(x => x.VariantsJson).HasColumnType("text");
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.Property(x => x.MetaTitle).HasMaxLength(255);
        builder.Property(x => x.MetaDescription).HasColumnType("text");
        builder.Property(x => x.DimensionsJson).HasColumnType("text");
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ProjectionVersion).IsRequired();

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.UpdatedAt);
    }
}
