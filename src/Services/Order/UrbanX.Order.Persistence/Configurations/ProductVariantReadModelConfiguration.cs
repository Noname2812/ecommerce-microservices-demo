using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class ProductVariantReadModelConfiguration : IEntityTypeConfiguration<ProductVariantReadModel>
{
    public void Configure(EntityTypeBuilder<ProductVariantReadModel> builder)
    {
        builder.ToTable(TableNames.ProductVariantView, TableNames.ReadSchema);

        builder.HasKey(x => x.VariantId);
        builder.Property(x => x.VariantId).ValueGeneratedNever();

        builder.Property(x => x.ProductId).IsRequired();
        builder.HasIndex(x => x.ProductId);

        builder.Property(x => x.ProductName).IsRequired().HasMaxLength(500);
        builder.Property(x => x.ProductIsActive).IsRequired();

        builder.Property(x => x.Sku).IsRequired().HasMaxLength(100);
        builder.Property(x => x.VariantName).HasMaxLength(500);
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);

        builder.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.Property(x => x.SellerId).IsRequired();
        builder.HasIndex(x => x.SellerId);
        builder.Property(x => x.SellerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.SellerIsActive).IsRequired();

        builder.Property(x => x.RowVersion).IsRequired();
        builder.Property(x => x.ProjectionVersion).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.DeletedAt);
    }
}
