using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.ReadModels;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations;

internal sealed class ProductListViewConfiguration : IEntityTypeConfiguration<ProductListView>
{
    public void Configure(EntityTypeBuilder<ProductListView> builder)
    {
        builder.ToTable(TableNames.ProductListView, TableNames.ReadSchema);
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
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ProjectionVersion).IsRequired();

        builder.HasIndex(x => new { x.SellerId, x.Status });
        builder.HasIndex(x => new { x.CategoryId, x.Status });
        builder.HasIndex(x => x.UpdatedAt);
    }
}
